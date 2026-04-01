namespace BetterPractice.SqlCipher.Tests;

[TestFixture]
public sealed class TransactionTests
{
    private DatabaseHandle? _db;
    private string? _dbPath;

    [SetUp]
    public async Task SetUp()
    {
        (_db, _dbPath) = await TestHelpers.OpenTempDb();
        await _db.Execute("CREATE TABLE accounts (id INTEGER PRIMARY KEY, balance INTEGER NOT NULL)");
        await _db.Execute("INSERT INTO accounts VALUES (1, 1000), (2, 500)");
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_db is not null) await _db.DisposeAsync();
        if (_dbPath is not null) TestHelpers.DeleteTempDb(_dbPath);
    }

    [Test]
    public async Task Transaction_CommitsOnSuccess()
    {
        await _db!.ExecuteTransaction(ctx =>
        {
            ctx.Execute("UPDATE accounts SET balance = balance - 100 WHERE id = 1");
            ctx.Execute("UPDATE accounts SET balance = balance + 100 WHERE id = 2");
        });

        var rows = await _db.Query("SELECT balance FROM accounts ORDER BY id");
        Assert.That(rows[0]["balance"].AsInt32(), Is.EqualTo(900));
        Assert.That(rows[1]["balance"].AsInt32(), Is.EqualTo(600));
    }

    [Test]
    public async Task Transaction_RollsBackOnException()
    {
        try
        {
            await _db!.ExecuteTransaction(ctx =>
            {
                ctx.Execute("UPDATE accounts SET balance = balance - 100 WHERE id = 1");
                throw new InvalidOperationException("simulated failure");
            });
        }
        catch (InvalidOperationException) { /* expected */ }

        var rows = await _db!.Query("SELECT balance FROM accounts ORDER BY id");
        Assert.That(rows[0]["balance"].AsInt32(), Is.EqualTo(1000));
        Assert.That(rows[1]["balance"].AsInt32(), Is.EqualTo(500));
    }

    [Test]
    public async Task Transaction_QueryWithinTransaction_SeesUncommittedChange()
    {
        await _db!.ExecuteTransaction(ctx =>
        {
            ctx.Execute("UPDATE accounts SET balance = 9999 WHERE id = 1");
            var rows = ctx.Query("SELECT balance FROM accounts WHERE id = 1");
            Assert.That(rows[0]["balance"].AsInt32(), Is.EqualTo(9999));
        });
    }

    [Test]
    public async Task Transaction_SequentialCalls_BothSucceed()
    {
        await _db!.ExecuteTransaction(ctx =>
            ctx.Execute("UPDATE accounts SET balance = balance + 1 WHERE id = 1"));

        await _db.ExecuteTransaction(ctx =>
            ctx.Execute("UPDATE accounts SET balance = balance + 1 WHERE id = 2"));

        var rows = await _db.Query("SELECT balance FROM accounts ORDER BY id");
        Assert.That(rows[0]["balance"].AsInt32(), Is.EqualTo(1001));
        Assert.That(rows[1]["balance"].AsInt32(), Is.EqualTo(501));
    }

    [Test]
    public async Task Transaction_ReturnValue_PassedThroughToCallerAsync()
    {
        int inserted = await _db!.ExecuteTransaction(ctx =>
            ctx.Execute("INSERT INTO accounts VALUES (3, 250)"));

        Assert.That(inserted, Is.EqualTo(1));
    }
}
