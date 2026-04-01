namespace BetterPractice.SqlCipher.Tests;

[TestFixture]
public sealed class DatabaseLifecycleTests
{
    private string? _dbPath;

    [TearDown]
    public void TearDown()
    {
        if (_dbPath is not null) TestHelpers.DeleteTempDb(_dbPath);
    }

    // -------------------------------------------------------------------------
    // Create / Open round-trip
    // -------------------------------------------------------------------------

    [Test]
    public async Task Create_NewFile_Succeeds()
    {
        var (db, path) = await TestHelpers.OpenTempDb();
        _dbPath = path;
        await using (db)
        {
            Assert.That(File.Exists(path), Is.True);
        }
    }

    [Test]
    public async Task Open_ExistingFile_WithCorrectPassphrase_Succeeds()
    {
        const string passphrase = "correct-passphrase";
        var (db, path) = await TestHelpers.OpenTempDb(passphrase);
        _dbPath = path;
        await db.DisposeAsync();

        await using var db2 = await DatabaseHandle.Open(path, passphrase);
        var result = await db2.Query("SELECT 1");
        Assert.That(result.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Open_ExistingFile_WithWrongPassphrase_Throws()
    {
        var (db, path) = await TestHelpers.OpenTempDb("correct");
        _dbPath = path;
        await db.DisposeAsync();

        Assert.ThrowsAsync<SqlCipherException>(
            async () => await DatabaseHandle.Open(path, "wrong-passphrase"));
    }

    [Test]
    public async Task Create_SetsWalMode()
    {
        var (db, path) = await TestHelpers.OpenTempDb();
        _dbPath = path;
        await using (db)
        {
            var result = await db.Query("PRAGMA journal_mode");
            Assert.That(result[0][0].AsText(), Is.EqualTo("wal").IgnoreCase);
        }
    }

    // -------------------------------------------------------------------------
    // Dispose
    // -------------------------------------------------------------------------

    [Test]
    public async Task DisposeAsync_DrainsPendingOperations()
    {
        var (db, path) = await TestHelpers.OpenTempDb();
        _dbPath = path;

        await db.Execute("CREATE TABLE t (x INTEGER)");

        // Queue several inserts then immediately dispose — drain should let them complete
        var tasks = Enumerable.Range(0, 20)
            .Select(i => db.Execute(
                "INSERT INTO t VALUES (@v)",
                new Dictionary<string, object?> { ["v"] = i }))
            .ToArray();

        await db.DisposeAsync();

        // Re-open to verify all inserts committed
        await using var db2 = await DatabaseHandle.Open(path, "test-passphrase-12345");
        var count = await db2.Query("SELECT COUNT(*) FROM t");
        Assert.That(count[0][0].AsInt32(), Is.EqualTo(20));
    }

    [Test]
    public async Task Operations_AfterDispose_Throw()
    {
        var (db, path) = await TestHelpers.OpenTempDb();
        _dbPath = path;
        await db.DisposeAsync();

        Assert.ThrowsAsync<ObjectDisposedException>(() => db.Execute("SELECT 1"));
    }

    // -------------------------------------------------------------------------
    // Rekey
    // -------------------------------------------------------------------------

    [Test]
    public async Task Rekey_ChangesPassphrase_NewPassphraseWorks()
    {
        const string original = "original-key";
        const string newKey   = "new-key";
        var (db, path) = await TestHelpers.OpenTempDb(original);
        _dbPath = path;

        await db.Execute("CREATE TABLE t (x TEXT)");
        await db.Execute(
            "INSERT INTO t VALUES (@v)",
            new Dictionary<string, object?> { ["v"] = "hello" });
        await db.Rekey(newKey);
        await db.DisposeAsync();

        // Old key should fail
        Assert.ThrowsAsync<SqlCipherException>(() => DatabaseHandle.Open(path, original));

        // New key should work
        await using var db2 = await DatabaseHandle.Open(path, newKey);
        var rows = await db2.Query("SELECT x FROM t");
        Assert.That(rows[0]["x"].AsText(), Is.EqualTo("hello"));
    }
}
