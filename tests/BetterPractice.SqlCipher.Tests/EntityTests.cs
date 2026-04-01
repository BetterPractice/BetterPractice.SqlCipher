namespace BetterPractice.SqlCipher.Tests;

// ---------------------------------------------------------------------------
// Test entity — the source generator picks this up and emits the mapper
// ---------------------------------------------------------------------------

[Table("contacts")]
public sealed class Contact : IEntity<int>
{
    [PrimaryKey]
    [Column("id")]
    public int Id { get; set; }

    [Column("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [Column("last_name")]
    public string LastName { get; set; } = string.Empty;

    [Column("email")]
    public string? Email { get; set; }

    [Column("score")]
    public double Score { get; set; }

    [NotMapped]
    public string FullName => $"{FirstName} {LastName}";
}

// ---------------------------------------------------------------------------

[TestFixture]
public sealed class EntityTests
{
    private DatabaseHandle? _db;
    private string? _dbPath;

    [SetUp]
    public async Task SetUp()
    {
        (_db, _dbPath) = await TestHelpers.OpenTempDb();
        await _db.Execute(
            CreateTable.Named("contacts")
                .Column("id",         ColumnType.Integer, ColumnConstraint.PrimaryKey, ColumnConstraint.AutoIncrement)
                .Column("first_name", ColumnType.Text,    ColumnConstraint.NotNull)
                .Column("last_name",  ColumnType.Text,    ColumnConstraint.NotNull)
                .Column("email",      ColumnType.Text)
                .Column("score",      ColumnType.Real)
                .Build());
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_db is not null) await _db.DisposeAsync();
        if (_dbPath is not null) TestHelpers.DeleteTempDb(_dbPath);
    }

    // -------------------------------------------------------------------------
    // Save (INSERT — auto-increment)
    // -------------------------------------------------------------------------

    [Test]
    public async Task Save_NewEntity_InsertsAndPopulatesId()
    {
        var contact = new Contact { FirstName = "Alice", LastName = "Smith", Score = 9.5 };
        contact = await _db!.Save<Contact, int>(contact);

        Assert.That(contact.Id, Is.GreaterThan(0));
    }

    [Test]
    public async Task Save_MultipleEntities_AssignsDistinctIds()
    {
        var c1 = await _db!.Save<Contact, int>(new Contact { FirstName = "Bob",   LastName = "A" });
        var c2 = await _db.Save<Contact, int>(new Contact  { FirstName = "Carol", LastName = "B" });

        Assert.That(c1.Id, Is.Not.EqualTo(c2.Id));
    }

    [Test]
    public async Task Save_ExistingEntity_UpdatesRecord()
    {
        var contact = await _db!.Save<Contact, int>(
            new Contact { FirstName = "Dave", LastName = "X", Score = 1.0 });

        contact.Score = 99.9;
        await _db.Save<Contact, int>(contact);

        var fetched = await _db.FetchOne<Contact, int>(contact.Id);
        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched!.Score, Is.EqualTo(99.9).Within(0.001));
    }

    // -------------------------------------------------------------------------
    // Fetch all
    // -------------------------------------------------------------------------

    [Test]
    public async Task Fetch_NoRows_ReturnsEmptyList()
    {
        var result = await _db!.Fetch<Contact, int>();
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task Fetch_AllRows_ReturnsAll()
    {
        await _db!.Save<Contact, int>(new Contact { FirstName = "E", LastName = "F" });
        await _db.Save<Contact, int>(new Contact  { FirstName = "G", LastName = "H" });

        var result = await _db.Fetch<Contact, int>();
        Assert.That(result.Count, Is.EqualTo(2));
    }

    // -------------------------------------------------------------------------
    // Fetch with expression
    // -------------------------------------------------------------------------

    [Test]
    public async Task Fetch_WithExpression_FiltersRows()
    {
        await _db!.Save<Contact, int>(new Contact { FirstName = "High", LastName = "Score", Score = 90.0 });
        await _db.Save<Contact, int>(new Contact  { FirstName = "Low",  LastName = "Score", Score = 10.0 });

        var high = await _db.Fetch<Contact, int>(new ColumnRef<double>("score") > 50.0);
        Assert.That(high.Count,         Is.EqualTo(1));
        Assert.That(high[0].FirstName,  Is.EqualTo("High"));
    }

    // -------------------------------------------------------------------------
    // FetchOne
    // -------------------------------------------------------------------------

    [Test]
    public async Task FetchOne_ExistingId_ReturnsEntity()
    {
        var contact = await _db!.Save<Contact, int>(
            new Contact { FirstName = "Ivan", LastName = "Y", Email = "ivan@example.com" });

        var fetched = await _db.FetchOne<Contact, int>(contact.Id);
        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched!.Email, Is.EqualTo("ivan@example.com"));
    }

    [Test]
    public async Task FetchOne_MissingId_ReturnsNull()
    {
        var result = await _db!.FetchOne<Contact, int>(9999);
        Assert.That(result, Is.Null);
    }

    // -------------------------------------------------------------------------
    // Delete by id
    // -------------------------------------------------------------------------

    [Test]
    public async Task Delete_ById_RemovesRow()
    {
        var contact = await _db!.Save<Contact, int>(new Contact { FirstName = "Jane", LastName = "Z" });
        await _db.Delete<Contact, int>(contact.Id);

        var result = await _db.FetchOne<Contact, int>(contact.Id);
        Assert.That(result, Is.Null);
    }

    // -------------------------------------------------------------------------
    // Delete by expression
    // -------------------------------------------------------------------------

    [Test]
    public async Task Delete_ByExpression_RemovesMatchingRows()
    {
        await _db!.Save<Contact, int>(new Contact { FirstName = "ToDelete", LastName = "A", Score = 1.0 });
        await _db.Save<Contact, int>(new Contact  { FirstName = "ToKeep",   LastName = "B", Score = 100.0 });

        await _db.Delete<Contact, int>(new ColumnRef<double>("score") < 50.0);

        var remaining = await _db.Fetch<Contact, int>();
        Assert.That(remaining.Count,        Is.EqualTo(1));
        Assert.That(remaining[0].FirstName, Is.EqualTo("ToKeep"));
    }

    // -------------------------------------------------------------------------
    // NotMapped attribute
    // -------------------------------------------------------------------------

    [Test]
    public async Task NotMapped_Property_NotPersistedOrQueried()
    {
        // FullName is [NotMapped] — saving/fetching should work without error
        var contact = await _db!.Save<Contact, int>(
            new Contact { FirstName = "Frank", LastName = "N" });
        var fetched = await _db.FetchOne<Contact, int>(contact.Id);

        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched!.FullName, Is.EqualTo("Frank N"));
    }

    // -------------------------------------------------------------------------
    // SaveAll
    // -------------------------------------------------------------------------

    [Test]
    public async Task SaveAll_InsertsAllEntities()
    {
        var contacts = new[]
        {
            new Contact { FirstName = "Batch1", LastName = "X" },
            new Contact { FirstName = "Batch2", LastName = "Y" },
            new Contact { FirstName = "Batch3", LastName = "Z" },
        };

        await _db!.SaveAll<Contact, int>(contacts);

        var all = await _db.Fetch<Contact, int>();
        Assert.That(all.Count, Is.EqualTo(3));
    }
}
