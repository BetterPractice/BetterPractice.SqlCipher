using System.Runtime.InteropServices;

namespace BetterPractice.SqlCipher.Internal;

/// <summary>
/// Wraps a raw sqlite3* connection handle. Owned exclusively by the Operation Queue
/// background task — never accessed from any other thread.
/// </summary>
internal sealed class DatabaseConnection : ISyncContext, IDisposable
{
    private IntPtr _db;
    private bool _disposed;

    internal DatabaseConnection(IntPtr db)
    {
        _db = db;
    }

    internal bool IsOpen => _db != IntPtr.Zero && !_disposed;

    // -------------------------------------------------------------------------
    // Execute — returns rows affected
    // -------------------------------------------------------------------------

    public int Execute(string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        IntPtr stmt = PrepareAndBind(sql, parameters);
        try
        {
            int rc = SqliteNative.sqlite3_step(stmt);
            if (rc != SqliteNative.SQLITE_DONE && rc != SqliteNative.SQLITE_ROW)
                ThrowError(rc, sql);
            return SqliteNative.sqlite3_changes(_db);
        }
        finally
        {
            SqliteNative.sqlite3_finalize(stmt);
        }
    }

    // -------------------------------------------------------------------------
    // Query — returns fully materialized ResultSet
    // -------------------------------------------------------------------------

    public ResultSet Query(string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        IntPtr stmt = PrepareAndBind(sql, parameters);
        try
        {
            int columnCount = SqliteNative.sqlite3_column_count(stmt);
            if (columnCount == 0)
                return ResultSet.Empty;

            string[] columnNames = new string[columnCount];
            for (int i = 0; i < columnCount; i++)
                columnNames[i] = SqliteNative.sqlite3_column_name(stmt, i);

            var rows = new List<Row>();
            int rc;
            while ((rc = SqliteNative.sqlite3_step(stmt)) == SqliteNative.SQLITE_ROW)
                rows.Add(ReadRow(stmt, columnNames, columnCount));

            if (rc != SqliteNative.SQLITE_DONE)
                ThrowError(rc, sql);

            return new ResultSet(columnNames, rows.ToArray());
        }
        finally
        {
            SqliteNative.sqlite3_finalize(stmt);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private IntPtr PrepareAndBind(string sql, IReadOnlyDictionary<string, object?>? parameters)
    {
        int rc = SqliteNative.sqlite3_prepare_v2(_db, sql, -1, out IntPtr stmt, out _);
        if (rc != SqliteNative.SQLITE_OK)
            ThrowError(rc, sql);

        if (parameters is { Count: > 0 })
            BindParameters(stmt, parameters);

        return stmt;
    }

    private void BindParameters(IntPtr stmt, IReadOnlyDictionary<string, object?> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            // SQLite named parameters use @name, :name, or $name prefix
            string paramName = name.StartsWith('@') || name.StartsWith(':') || name.StartsWith('$')
                ? name
                : "@" + name;

            int index = SqliteNative.sqlite3_bind_parameter_index(stmt, paramName);
            if (index == 0)
                throw new ArgumentException($"Parameter '{name}' not found in SQL statement.");

            BindValue(stmt, index, value);
        }
    }

    private void BindValue(IntPtr stmt, int index, object? value)
    {
        int rc = value switch
        {
            null                  => SqliteNative.sqlite3_bind_null(stmt, index),
            bool b                => SqliteNative.sqlite3_bind_int64(stmt, index, b ? 1L : 0L),
            byte v                => SqliteNative.sqlite3_bind_int64(stmt, index, v),
            short v               => SqliteNative.sqlite3_bind_int64(stmt, index, v),
            int v                 => SqliteNative.sqlite3_bind_int64(stmt, index, v),
            long v                => SqliteNative.sqlite3_bind_int64(stmt, index, v),
            float v               => SqliteNative.sqlite3_bind_double(stmt, index, v),
            double v              => SqliteNative.sqlite3_bind_double(stmt, index, v),
            string v              => SqliteNative.sqlite3_bind_text(stmt, index, v, -1, SqliteNative.SQLITE_TRANSIENT),
            byte[] v              => SqliteNative.sqlite3_bind_blob(stmt, index, v, v.Length, SqliteNative.SQLITE_TRANSIENT),
            _ => throw new ArgumentException(
                $"Unsupported parameter type: {value.GetType().FullName}. " +
                $"Supported types: null, bool, byte, short, int, long, float, double, string, byte[].")
        };

        if (rc != SqliteNative.SQLITE_OK)
            ThrowError(rc, context: "bind parameter");
    }

    private static Row ReadRow(IntPtr stmt, string[] columnNames, int columnCount)
    {
        var values = new Value[columnCount];
        for (int i = 0; i < columnCount; i++)
            values[i] = ReadValue(stmt, i);
        return new Row(columnNames, values);
    }

    private static Value ReadValue(IntPtr stmt, int col)
    {
        int type = SqliteNative.sqlite3_column_type(stmt, col);
        return type switch
        {
            SqliteNative.SQLITE_INTEGER => new Value(SqliteNative.sqlite3_column_int64(stmt, col)),
            SqliteNative.SQLITE_FLOAT   => new Value(SqliteNative.sqlite3_column_double(stmt, col)),
            SqliteNative.SQLITE_TEXT    => new Value(SqliteNative.sqlite3_column_text(stmt, col) ?? string.Empty),
            SqliteNative.SQLITE_BLOB    => ReadBlob(stmt, col),
            SqliteNative.SQLITE_NULL    => Value.Null,
            _                          => Value.Null
        };
    }

    private static Value ReadBlob(IntPtr stmt, int col)
    {
        int length = SqliteNative.sqlite3_column_bytes(stmt, col);
        if (length == 0)
            return new Value(Array.Empty<byte>());

        IntPtr ptr = SqliteNative.sqlite3_column_blob(stmt, col);
        byte[] blob = new byte[length];
        Marshal.Copy(ptr, blob, 0, length);
        return new Value(blob);
    }

    private void ThrowError(int resultCode, string? sql = null, string? context = null)
    {
        string message = _db != IntPtr.Zero
            ? SqliteNative.sqlite3_errmsg(_db)
            : "No database connection.";

        string ctx = context ?? sql ?? "unknown";
        throw new SqlCipherException(resultCode, message, ctx);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_db != IntPtr.Zero)
        {
            SqliteNative.sqlite3_close_v2(_db);
            _db = IntPtr.Zero;
        }
    }
}
