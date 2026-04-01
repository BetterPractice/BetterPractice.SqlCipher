using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using BetterPractice.SqlCipher.Internal;

namespace BetterPractice.SqlCipher;

/// <summary>
/// The primary handle for interacting with an encrypted SQLCipher database.
/// All operations are serialized through an internal Operation Queue — safe to use
/// from multiple threads without external synchronization.
/// Obtain instances via <see cref="Create"/> or <see cref="Open"/>; dispose when done.
/// </summary>
public sealed class DatabaseHandle : IDisposable, IAsyncDisposable
{
    private readonly DatabaseConnection _connection;
    private readonly Channel<Operation> _queue;
    private readonly Task _consumer;
    private volatile bool _disposed;

    // -------------------------------------------------------------------------
    // Construction (private — use static factories)
    // -------------------------------------------------------------------------

    private DatabaseHandle(DatabaseConnection connection, string path, CipherConfiguration cipherConfiguration)
    {
        _connection       = connection;
        Path              = path;
        CipherConfiguration = cipherConfiguration;
        _queue            = Channel.CreateUnbounded<Operation>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _consumer = Task.Run(ConsumeLoop);
    }

    // -------------------------------------------------------------------------
    // Static factory methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a new encrypted database file at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">Absolute path where the file will be created. Must not already exist.</param>
    /// <param name="passphrase">Passphrase used to encrypt the database. Must not be null or empty.</param>
    /// <param name="cipherConfiguration">Cipher parameters. If null, <see cref="CipherConfiguration.Default"/> is used.</param>
    public static Task<DatabaseHandle> Create(
        string path,
        string passphrase,
        CipherConfiguration? cipherConfiguration = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        if (File.Exists(path))
            throw new InvalidOperationException($"Database file already exists: {path}");

        string? directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Directory does not exist: {directory}");

        var config = cipherConfiguration ?? CipherConfiguration.Default;

        return Task.Run(() =>
        {
            DatabaseConnection connection = OpenNativeConnection(path,
                SqliteNative.SQLITE_OPEN_READWRITE | SqliteNative.SQLITE_OPEN_CREATE);
            try
            {
                ApplyKey(connection, passphrase);
                ApplyCipherConfiguration(connection, config);
                EnableWalMode(connection);
                InitializeSentinelTable(connection);
                return new DatabaseHandle(connection, path, config);
            }
            catch
            {
                connection.Dispose();
                TryDeleteFile(path);
                throw;
            }
        });
    }

    /// <summary>
    /// Opens an existing encrypted database file at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">Absolute path to an existing database file.</param>
    /// <param name="passphrase">Passphrase used to decrypt the database. Must not be null or empty.</param>
    public static Task<DatabaseHandle> Open(string path, string passphrase)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Database file not found: {path}", path);

        return Task.Run(() =>
        {
            DatabaseConnection connection = OpenNativeConnection(path,
                SqliteNative.SQLITE_OPEN_READWRITE);
            try
            {
                ApplyKey(connection, passphrase);
                VerifyKey(connection, path);
                VerifyWalMode(connection, path);
                return new DatabaseHandle(connection, path, CipherConfiguration.Default);
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        });
    }

    // -------------------------------------------------------------------------
    // Properties
    // -------------------------------------------------------------------------

    /// <summary>Absolute path to the database file.</summary>
    public string Path { get; }

    /// <summary>Cipher configuration. On Open, reflects library defaults — actual stored config is in the file header.</summary>
    public CipherConfiguration CipherConfiguration { get; }

    /// <summary>True if this handle has been disposed.</summary>
    public bool IsDisposed => _disposed;

    // -------------------------------------------------------------------------
    // Public operations — all enqueue to the Operation Queue
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executes a SQL statement that returns no rows.
    /// </summary>
    /// <returns>Number of rows affected.</returns>
    public Task<int> Execute(string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sql);
        ThrowIfDisposed();

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new ExecuteOperation(sql, parameters, tcs));
        return tcs.Task;
    }

    /// <summary>
    /// Executes a SQL statement that returns rows.
    /// </summary>
    /// <returns>A fully materialized <see cref="ResultSet"/>.</returns>
    public Task<ResultSet> Query(string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(sql);
        ThrowIfDisposed();

        var tcs = new TaskCompletionSource<ResultSet>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new QueryOperation(sql, parameters, tcs));
        return tcs.Task;
    }

    /// <summary>Executes a pre-built query that returns no rows.</summary>
    public Task<int> Execute(BuiltQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Execute(query.Sql, query.Bindings);
    }

    /// <summary>Executes a pre-built query that returns rows.</summary>
    public Task<ResultSet> Query(BuiltQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Query(query.Sql, query.Bindings);
    }

    /// <summary>Executes a CREATE TABLE statement. Bypasses statement cache.</summary>
    public Task Execute(CreateTable statement) => Execute(statement.Build());

    /// <summary>Executes an ALTER TABLE statement. Bypasses statement cache.</summary>
    public Task Execute(AlterTable statement) => Execute(statement.Build());

    /// <summary>Executes a DROP TABLE statement. Bypasses statement cache.</summary>
    public Task Execute(DropTable statement) => Execute(statement.Build());

    /// <summary>Executes a CREATE INDEX statement. Bypasses statement cache.</summary>
    public Task Execute(CreateIndex statement) => Execute(statement.Build());

    /// <summary>Executes a DROP INDEX statement. Bypasses statement cache.</summary>
    public Task Execute(DropIndex statement) => Execute(statement.Build());

    /// <summary>
    /// Changes the passphrase on the database. Rewrites the entire database file.
    /// This is an expensive operation proportional to file size.
    /// </summary>
    public Task Rekey(string newPassphrase)
    {
        ArgumentException.ThrowIfNullOrEmpty(newPassphrase);
        ThrowIfDisposed();

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new CompositeOperation<bool>(conn =>
        {
            conn.Execute($"PRAGMA rekey = '{EscapePassphrase(newPassphrase)}'");
            return true;
        }, tcs));
        return tcs.Task;
    }

    /// <summary>
    /// Executes a group of operations atomically within a single transaction.
    /// All operations in <paramref name="body"/> run inline — no other operations can interleave.
    /// Commits if <paramref name="body"/> completes; rolls back if it throws.
    /// </summary>
    public Task ExecuteTransaction(Func<ITransactionContext, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ThrowIfDisposed();

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new CompositeOperation<bool>(conn =>
        {
            var context = new TransactionContext(conn);
            conn.Execute("BEGIN");
            try
            {
                body(context).GetAwaiter().GetResult();
                conn.Execute("COMMIT");
                return true;
            }
            catch (Exception bodyException)
            {
                try { conn.Execute("ROLLBACK"); }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(bodyException, rollbackException);
                }
                throw;
            }
        }, tcs));
        return tcs.Task;
    }

    /// <summary>
    /// Applies all pending migrations in the order provided. Safe to call on every launch.
    /// </summary>
    public Task Migrate(IReadOnlyList<IMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        ThrowIfDisposed();

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new CompositeOperation<bool>(conn =>
        {
            MigrationRunner.Migrate(conn, migrations);
            return true;
        }, tcs));
        return tcs.Task;
    }

    /// <summary>
    /// Rolls back all migrations applied after <paramref name="targetId"/>.
    /// <paramref name="targetId"/> itself remains applied.
    /// </summary>
    public Task Rollback(string targetId, IReadOnlyList<IMigration> migrations)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetId);
        ArgumentNullException.ThrowIfNull(migrations);
        ThrowIfDisposed();

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new CompositeOperation<bool>(conn =>
        {
            MigrationRunner.Rollback(conn, targetId, migrations);
            return true;
        }, tcs));
        return tcs.Task;
    }

    // -------------------------------------------------------------------------
    // Internal generic entity methods (prefer generated typed extension methods)
    // -------------------------------------------------------------------------

    public Task<T> Save<T, TId>(T entity)
        where T : IEntity<TId>
        where TId : notnull
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new CompositeOperation<T>(conn =>
        {
            var mapper = EntityMapperRegistry.GetMapper<T, TId>();
            return mapper.Save(conn, entity);
        }, tcs));
        return tcs.Task;
    }

    public Task<IReadOnlyList<T>> SaveAll<T, TId>(IReadOnlyList<T> entities)
        where T : IEntity<TId>
        where TId : notnull
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<IReadOnlyList<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new CompositeOperation<IReadOnlyList<T>>(conn =>
        {
            var mapper = EntityMapperRegistry.GetMapper<T, TId>();
            conn.Execute("BEGIN");
            try
            {
                var results = new List<T>(entities.Count);
                foreach (var entity in entities)
                    results.Add(mapper.Save(conn, entity));
                conn.Execute("COMMIT");
                return results;
            }
            catch
            {
                try { conn.Execute("ROLLBACK"); } catch { /* best effort */ }
                throw;
            }
        }, tcs));
        return tcs.Task;
    }

    public Task<IReadOnlyList<T>> Fetch<T, TId>()
        where T : IEntity<TId>
        where TId : notnull
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<IReadOnlyList<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new CompositeOperation<IReadOnlyList<T>>(conn =>
            EntityMapperRegistry.GetMapper<T, TId>().FetchAll(conn), tcs));
        return tcs.Task;
    }

    public Task<IReadOnlyList<T>> Fetch<T, TId>(IExpression where, params ParamBinding[] bindings)
        where T : IEntity<TId>
        where TId : notnull
    {
        ThrowIfDisposed();
        var (whereSql, parameters) = ExpressionRenderer.Render(where, bindings);
        var tcs = new TaskCompletionSource<IReadOnlyList<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new CompositeOperation<IReadOnlyList<T>>(conn =>
            EntityMapperRegistry.GetMapper<T, TId>().FetchWhere(conn, whereSql, parameters), tcs));
        return tcs.Task;
    }

    public Task<T?> FetchOne<T, TId>(TId id)
        where T : IEntity<TId>
        where TId : notnull
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new CompositeOperation<T?>(conn =>
            EntityMapperRegistry.GetMapper<T, TId>().FetchOne(conn, id), tcs));
        return tcs.Task;
    }

    public Task<int> Delete<T, TId>(TId id)
        where T : IEntity<TId>
        where TId : notnull
    {
        ThrowIfDisposed();
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new CompositeOperation<int>(conn =>
            EntityMapperRegistry.GetMapper<T, TId>().Delete(conn, id), tcs));
        return tcs.Task;
    }

    public Task<int> Delete<T, TId>(IExpression where, params ParamBinding[] bindings)
        where T : IEntity<TId>
        where TId : notnull
    {
        ThrowIfDisposed();
        var (whereSql, parameters) = ExpressionRenderer.Render(where, bindings);
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new CompositeOperation<int>(conn =>
            EntityMapperRegistry.GetMapper<T, TId>().DeleteWhere(conn, whereSql, parameters), tcs));
        return tcs.Task;
    }

    // -------------------------------------------------------------------------
    // Disposal
    // -------------------------------------------------------------------------

    /// <summary>
    /// Synchronously disposes. In-flight queued operations receive ObjectDisposedException.
    /// Prefer <see cref="DisposeAsync"/> to allow the queue to drain cleanly.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.Writer.TryComplete();
        _connection.Dispose();
    }

    /// <summary>
    /// Asynchronously disposes. Completes the queue and awaits the consumer task,
    /// allowing already-queued operations to finish before the connection is closed.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.Writer.TryComplete();
        await _consumer.ConfigureAwait(false);
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void Enqueue(Operation operation)
    {
        if (!_queue.Writer.TryWrite(operation))
            throw new ObjectDisposedException(nameof(DatabaseHandle),
                "Cannot submit operations to a disposed DatabaseHandle.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DatabaseHandle));
    }

    private async Task ConsumeLoop()
    {
        await foreach (Operation operation in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            operation.Execute(_connection);
        }
    }

    // -------------------------------------------------------------------------
    // Static setup helpers (run before the queue starts)
    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    // Native library resolver — ensures libsqlcipher is found next to the assembly
    // -------------------------------------------------------------------------

    static DatabaseHandle()
    {
        NativeLibrary.SetDllImportResolver(typeof(DatabaseHandle).Assembly, ResolveNativeLibrary);
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != SqliteNative.LibraryName)
            return IntPtr.Zero;

        string baseDir = AppContext.BaseDirectory;

        // macOS
        string dylib = System.IO.Path.Combine(baseDir, $"lib{libraryName}.dylib");
        if (File.Exists(dylib) && NativeLibrary.TryLoad(dylib, out IntPtr handle))
            return handle;

        // Linux
        string so = System.IO.Path.Combine(baseDir, $"lib{libraryName}.so");
        if (File.Exists(so) && NativeLibrary.TryLoad(so, out handle))
            return handle;

        // Windows
        string dll = System.IO.Path.Combine(baseDir, $"{libraryName}.dll");
        if (File.Exists(dll) && NativeLibrary.TryLoad(dll, out handle))
            return handle;

        // Fall through to default resolution
        return IntPtr.Zero;
    }

    private static DatabaseConnection OpenNativeConnection(string path, int flags)
    {
        int rc = SqliteNative.sqlite3_open_v2(path, out IntPtr db, flags, IntPtr.Zero);
        if (rc != SqliteNative.SQLITE_OK)
        {
            string message = db != IntPtr.Zero
                ? SqliteNative.sqlite3_errmsg(db)
                : $"sqlite3_open_v2 returned {rc}";
            if (db != IntPtr.Zero)
                SqliteNative.sqlite3_close_v2(db);
            throw new SqlCipherException(rc, message, path);
        }
        return new DatabaseConnection(db);
    }

    private static void ApplyKey(DatabaseConnection conn, string passphrase) =>
        conn.Execute($"PRAGMA key = '{EscapePassphrase(passphrase)}'");

    private static void ApplyCipherConfiguration(DatabaseConnection conn, CipherConfiguration config)
    {
        conn.Execute($"PRAGMA cipher_page_size = {config.PageSize}");
        conn.Execute($"PRAGMA kdf_iter = {config.KdfIterations}");
        conn.Execute($"PRAGMA cipher_kdf_algorithm = {config.KdfAlgorithmPragmaValue}");
        conn.Execute($"PRAGMA cipher_hmac_algorithm = {config.HmacAlgorithmPragmaValue}");
    }

    private static void EnableWalMode(DatabaseConnection conn)
    {
        var result = conn.Query("PRAGMA journal_mode = WAL");
        // The PRAGMA returns the resulting journal mode
        if (result.Count == 0 || result[0][0].AsText() != "wal")
            throw new InvalidOperationException("Failed to enable WAL mode.");
    }

    private static void VerifyKey(DatabaseConnection conn, string path)
    {
        try
        {
            conn.Execute("SELECT count(*) FROM sqlite_master");
        }
        catch (SqlCipherException ex) when (
            ex.SqliteResultCode == SqliteNative.SQLITE_NOTADB ||
            ex.SqliteResultCode == SqliteNative.SQLITE_ERROR)
        {
            throw new SqlCipherException(ex.SqliteResultCode,
                "Passphrase is incorrect or the file is not a valid SQLCipher database.",
                path);
        }
    }

    private static void VerifyWalMode(DatabaseConnection conn, string path)
    {
        var result = conn.Query("PRAGMA journal_mode");
        if (result.Count == 0 || result[0][0].AsText() != "wal")
            throw new InvalidOperationException(
                $"Database at '{path}' is not in WAL mode. " +
                "Only databases created by BetterPractice.SqlCipher are supported.");
    }

    private static void InitializeSentinelTable(DatabaseConnection conn) =>
        conn.Execute(
            "CREATE TABLE IF NOT EXISTS _bp_meta (k TEXT PRIMARY KEY, v TEXT)");

    private static string EscapePassphrase(string passphrase) =>
        passphrase.Replace("'", "''");

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
