using System.Security.Cryptography;

namespace BetterPractice.SqlCipher.Tests;

[TestFixture]
public sealed class RawKeyAndJournalModeTests
{
    private string? _dbPath;

    [TearDown]
    public void TearDown()
    {
        if (_dbPath is not null) TestHelpers.DeleteTempDb(_dbPath);
    }

    private static byte[] NewRawKey() => RandomNumberGenerator.GetBytes(DatabaseHandle.RawKeyLength);

    // -------------------------------------------------------------------------
    // Raw key
    // -------------------------------------------------------------------------

    [Test]
    public async Task RawKey_CreateAndReopen_RoundTrips()
    {
        _dbPath = TestHelpers.TempFilePath();
        byte[] key = NewRawKey();
        await using (var db = await DatabaseHandle.Create(_dbPath, key))
        {
            Assert.That(db.IsEncrypted, Is.True);
            await db.Execute("CREATE TABLE t (x TEXT)");
            await db.Execute("INSERT INTO t VALUES ('raw')");
        }

        await using var reopened = await DatabaseHandle.Open(_dbPath, key);
        var rows = await reopened.Query("SELECT x FROM t");
        Assert.That(rows[0][0].AsText(), Is.EqualTo("raw"));
    }

    [Test]
    public async Task RawKey_WrongKeyOrPassphrase_Throws()
    {
        _dbPath = TestHelpers.TempFilePath();
        byte[] key = NewRawKey();
        await using (await DatabaseHandle.Create(_dbPath, key)) { }

        Assert.ThrowsAsync<SqlCipherException>(() => DatabaseHandle.Open(_dbPath, NewRawKey()));
        Assert.ThrowsAsync<SqlCipherException>(() => DatabaseHandle.Open(_dbPath, Convert.ToBase64String(key)));
        Assert.ThrowsAsync<SqlCipherException>(() => DatabaseHandle.OpenUnencrypted(_dbPath));
    }

    [Test]
    public void RawKey_WrongLength_ThrowsBeforeAnyNativeCall()
    {
        string path = TestHelpers.TempFilePath();
        Assert.Throws<ArgumentException>(() => DatabaseHandle.Create(path, RandomNumberGenerator.GetBytes(31)));
        Assert.Throws<ArgumentException>(() => DatabaseHandle.Open(path, RandomNumberGenerator.GetBytes(48)));
        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public async Task RawKey_OpensWithoutMeasurableKeyDerivation()
    {
        _dbPath = TestHelpers.TempFilePath();
        byte[] key = NewRawKey();
        await using (await DatabaseHandle.Create(_dbPath, key)) { }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 5; i++)
            await using (await DatabaseHandle.Open(_dbPath, key)) { }
        sw.Stop();

        // Five passphrase opens would spend well over a second in PBKDF2 at 256k iterations.
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(500));
    }

    [Test]
    public async Task Rekey_BetweenPassphraseAndRawKey_BothDirections()
    {
        _dbPath = TestHelpers.TempFilePath();
        byte[] key = NewRawKey();
        await using (var db = await DatabaseHandle.Create(_dbPath, "pass"))
        {
            await db.Execute("CREATE TABLE t (x TEXT)");
            await db.Rekey(key);
        }
        await using (var db = await DatabaseHandle.Open(_dbPath, key))
        {
            await db.Execute("INSERT INTO t VALUES ('after')");
            await db.Rekey("pass2");
        }
        await using var final = await DatabaseHandle.Open(_dbPath, "pass2");
        Assert.That((await final.Query("SELECT x FROM t"))[0][0].AsText(), Is.EqualTo("after"));
    }

    // -------------------------------------------------------------------------
    // Journal mode
    // -------------------------------------------------------------------------

    [Test]
    public async Task Rollback_IsASingleFile_AndIsReportedOnReopen()
    {
        _dbPath = TestHelpers.TempFilePath();
        await using (var db = await DatabaseHandle.CreateUnencrypted(_dbPath, JournalMode.Rollback))
        {
            Assert.That(db.JournalMode, Is.EqualTo(JournalMode.Rollback));
            await db.Execute("CREATE TABLE t (x TEXT)");
            await db.Execute("INSERT INTO t VALUES ('one')");
            Assert.That(File.Exists(_dbPath + "-wal"), Is.False);
            Assert.That(File.Exists(_dbPath + "-shm"), Is.False);
        }

        await using var reopened = await DatabaseHandle.OpenUnencrypted(_dbPath);
        Assert.That(reopened.JournalMode, Is.EqualTo(JournalMode.Rollback));
        Assert.That((await reopened.Query("PRAGMA journal_mode"))[0][0].AsText(), Is.EqualTo("delete").IgnoreCase);
    }

    [Test]
    public async Task Rollback_WorksForEncryptedDatabasesToo()
    {
        _dbPath = TestHelpers.TempFilePath();
        byte[] key = NewRawKey();
        await using (var db = await DatabaseHandle.Create(_dbPath, key, journalMode: JournalMode.Rollback))
        {
            await db.Execute("CREATE TABLE t (x TEXT)");
            Assert.That(File.Exists(_dbPath + "-wal"), Is.False);
        }
        await using var reopened = await DatabaseHandle.Open(_dbPath, key);
        Assert.That(reopened.JournalMode, Is.EqualTo(JournalMode.Rollback));
    }

    [Test]
    public async Task Wal_IsTheDefault_AndIsReportedOnReopen()
    {
        var (db, path) = await TestHelpers.OpenTempDb();
        _dbPath = path;
        await using (db)
        {
            Assert.That(db.JournalMode, Is.EqualTo(JournalMode.Wal));
        }
        await using var reopened = await DatabaseHandle.Open(path, "test-passphrase-12345");
        Assert.That(reopened.JournalMode, Is.EqualTo(JournalMode.Wal));
    }

    [Test]
    public async Task Open_NeverChangesTheJournalMode()
    {
        _dbPath = TestHelpers.TempFilePath();
        await using (await DatabaseHandle.CreateUnencrypted(_dbPath, JournalMode.Rollback)) { }
        await using (await DatabaseHandle.OpenUnencrypted(_dbPath)) { }
        await using var again = await DatabaseHandle.OpenUnencrypted(_dbPath);
        Assert.That(again.JournalMode, Is.EqualTo(JournalMode.Rollback));
    }
}
