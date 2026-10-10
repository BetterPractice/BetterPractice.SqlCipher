using System.Text;

namespace BetterPractice.SqlCipher.Tests;

[TestFixture]
public sealed class UnencryptedDatabaseTests
{
    private const string SqliteMagic = "SQLite format 3";

    private string? _dbPath;

    [TearDown]
    public void TearDown()
    {
        if (_dbPath is not null) TestHelpers.DeleteTempDb(_dbPath);
    }

    [Test]
    public async Task CreateUnencrypted_ProducesAPlainSqliteFile_ThatRoundTrips()
    {
        _dbPath = TestHelpers.TempFilePath();
        await using (var db = await DatabaseHandle.CreateUnencrypted(_dbPath))
        {
            Assert.That(db.IsEncrypted, Is.False);
            await db.Execute("CREATE TABLE t (x TEXT)");
            await db.Execute("INSERT INTO t VALUES ('hello')");
        }

        byte[] header = new byte[SqliteMagic.Length];
        using (var f = File.OpenRead(_dbPath)) f.ReadExactly(header);
        Assert.That(Encoding.ASCII.GetString(header), Is.EqualTo(SqliteMagic));

        await using var reopened = await DatabaseHandle.OpenUnencrypted(_dbPath);
        Assert.That(reopened.IsEncrypted, Is.False);
        var rows = await reopened.Query("SELECT x FROM t");
        Assert.That(rows[0][0].AsText(), Is.EqualTo("hello"));
    }

    [Test]
    public async Task OpenUnencrypted_OnAnEncryptedFile_Throws()
    {
        var (db, path) = await TestHelpers.OpenTempDb("secret");
        _dbPath = path;
        await db.DisposeAsync();

        Assert.ThrowsAsync<SqlCipherException>(() => DatabaseHandle.OpenUnencrypted(path));
    }

    [Test]
    public async Task Open_WithPassphrase_OnAnUnencryptedFile_Throws()
    {
        _dbPath = TestHelpers.TempFilePath();
        await using (await DatabaseHandle.CreateUnencrypted(_dbPath)) { }

        Assert.ThrowsAsync<SqlCipherException>(() => DatabaseHandle.Open(_dbPath, "any"));
    }

    [Test]
    public async Task Rekey_OnAnUnencryptedDatabase_Throws()
    {
        _dbPath = TestHelpers.TempFilePath();
        await using var db = await DatabaseHandle.CreateUnencrypted(_dbPath);

        Assert.ThrowsAsync<InvalidOperationException>(() => db.Rekey("new"));
    }

    [Test]
    public async Task EncryptedHandles_ReportIsEncrypted()
    {
        var (db, path) = await TestHelpers.OpenTempDb();
        _dbPath = path;
        await using (db)
        {
            Assert.That(db.IsEncrypted, Is.True);
        }
    }
}
