namespace BetterPractice.SqlCipher.Tests;

[TestFixture]
public sealed class ExecuteQueryTests
{
    private DatabaseHandle? _db;
    private string? _dbPath;

    [SetUp]
    public async Task SetUp()
    {
        (_db, _dbPath) = await TestHelpers.OpenTempDb();
        await _db.Execute("CREATE TABLE items (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, value REAL, flag INTEGER)");
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_db is not null) await _db.DisposeAsync();
        if (_dbPath is not null) TestHelpers.DeleteTempDb(_dbPath);
    }

    private static IReadOnlyDictionary<string, object?> P(params (string Name, object? Value)[] pairs)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in pairs)
            dict[name] = value;
        return dict;
    }

    // -------------------------------------------------------------------------
    // Execute
    // -------------------------------------------------------------------------

    [Test]
    public async Task Execute_Insert_ReturnsRowsAffected()
    {
        int rows = await _db!.Execute(
            "INSERT INTO items (name, value, flag) VALUES (@n, @v, @f)",
            P(("n", "a"), ("v", 1.5), ("f", 1)));
        Assert.That(rows, Is.EqualTo(1));
    }

    [Test]
    public async Task Execute_WithNamedParams_BindsCorrectly()
    {
        await _db!.Execute(
            "INSERT INTO items (name, value) VALUES (@n, @v)",
            P(("n", "hello"), ("v", 3.14)));

        var rows = await _db.Query("SELECT name, value FROM items");
        Assert.That(rows[0]["name"].AsText(),   Is.EqualTo("hello"));
        Assert.That(rows[0]["value"].AsDouble(), Is.EqualTo(3.14).Within(0.001));
    }

    [Test]
    public async Task Execute_NullParam_StoresNull()
    {
        await _db!.Execute(
            "INSERT INTO items (name, value) VALUES (@n, @v)",
            P(("n", "nulltest"), ("v", null)));

        var rows = await _db.Query("SELECT value FROM items WHERE name = 'nulltest'");
        Assert.That(rows[0]["value"].IsNull, Is.True);
    }

    // -------------------------------------------------------------------------
    // Query
    // -------------------------------------------------------------------------

    [Test]
    public async Task Query_EmptyTable_ReturnsEmptyResultSet()
    {
        var result = await _db!.Query("SELECT * FROM items");
        Assert.That(result.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task Query_MultipleRows_AllReturned()
    {
        for (int i = 0; i < 5; i++)
            await _db!.Execute(
                "INSERT INTO items (name) VALUES (@n)",
                P(("n", $"item{i}")));

        var result = await _db!.Query("SELECT name FROM items ORDER BY id");
        Assert.That(result.Count, Is.EqualTo(5));
        for (int i = 0; i < 5; i++)
            Assert.That(result[i]["name"].AsText(), Is.EqualTo($"item{i}"));
    }

    [Test]
    public async Task Query_ColumnAccess_ByNameAndIndex()
    {
        await _db!.Execute(
            "INSERT INTO items (name, value, flag) VALUES (@n, @v, @f)",
            P(("n", "x"), ("v", 9.9), ("f", 1)));

        var rows = await _db.Query("SELECT name, value, flag FROM items");
        var row  = rows[0];

        Assert.That(row["name"].AsText(),   Is.EqualTo("x"));
        Assert.That(row["value"].AsDouble(), Is.EqualTo(9.9).Within(0.001));
        Assert.That(row["flag"].AsBool(),   Is.True);
        Assert.That(row[0].AsText(),        Is.EqualTo("x")); // index access
    }

    [Test]
    public async Task Query_TryGet_MissingColumn_ReturnsFalse()
    {
        await _db!.Execute("INSERT INTO items (name) VALUES (@n)", P(("n", "y")));
        var rows  = await _db.Query("SELECT name FROM items");
        var found = rows[0].TryGet("nonexistent");
        Assert.That(found, Is.Null);
    }

    // -------------------------------------------------------------------------
    // Value type round-trips
    // -------------------------------------------------------------------------

    [Test]
    public async Task Value_Int64_RoundTrip()
    {
        await _db!.Execute("CREATE TABLE t_int (v INTEGER)");
        await _db.Execute("INSERT INTO t_int VALUES (@v)", P(("v", long.MaxValue)));
        var rows = await _db.Query("SELECT v FROM t_int");
        Assert.That(rows[0]["v"].AsInt64(), Is.EqualTo(long.MaxValue));
    }

    [Test]
    public async Task Value_Blob_RoundTrip()
    {
        await _db!.Execute("CREATE TABLE t_blob (v BLOB)");
        byte[] data = { 1, 2, 3, 0xFF, 0x00 };
        await _db.Execute("INSERT INTO t_blob VALUES (@v)", P(("v", data)));
        var rows = await _db.Query("SELECT v FROM t_blob");
        Assert.That(rows[0]["v"].AsBlob().ToArray(), Is.EqualTo(data));
    }

    // -------------------------------------------------------------------------
    // BuiltQuery overloads
    // -------------------------------------------------------------------------

    [Test]
    public async Task Execute_BuiltQuery_Works()
    {
        var query = Insert.Into("items")
            .Set("name", "built")
            .Set("value", 42.0)
            .Build();

        int rows = await _db!.Execute(query);
        Assert.That(rows, Is.EqualTo(1));
    }

    [Test]
    public async Task Query_BuiltQuery_Works()
    {
        await _db!.Execute(
            "INSERT INTO items (name, value) VALUES (@n1, @v1), (@n2, @v2)",
            P(("n1", "q1"), ("v1", 10), ("n2", "q2"), ("v2", 20)));

        var query = Select.From("items")
            .Columns("name", "value")
            .OrderBy("value", SortDirection.Descending)
            .Build();

        var result = await _db.Query(query);
        Assert.That(result.Count,         Is.EqualTo(2));
        Assert.That(result[0]["name"].AsText(), Is.EqualTo("q2"));
    }
}
