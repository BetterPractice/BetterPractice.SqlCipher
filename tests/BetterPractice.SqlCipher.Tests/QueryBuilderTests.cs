namespace BetterPractice.SqlCipher.Tests;

/// <summary>
/// Tests for the fluent query builder types.
/// Pure unit tests — only calls Build() and inspects Sql and Bindings.
/// No database connection needed.
/// </summary>
[TestFixture]
public sealed class QueryBuilderTests
{
    // =========================================================================
    // Select
    // =========================================================================

    [Test]
    public void Select_AllColumns_EmitsStarFromClause()
    {
        var q = Select.From("users").Build();
        Assert.That(q.Sql, Is.EqualTo("SELECT * FROM users"));
        Assert.That(q.Bindings, Is.Empty);
    }

    [Test]
    public void Select_WithAlias_EmitsAsClause()
    {
        var q = Select.From("users", "u").Columns("u.id", "u.name").Build();
        Assert.That(q.Sql, Is.EqualTo("SELECT u.id, u.name FROM users AS u"));
    }

    [Test]
    public void Select_Distinct_EmitsDistinctKeyword()
    {
        var q = Select.From("users").Distinct().Build();
        Assert.That(q.Sql, Does.StartWith("SELECT DISTINCT"));
    }

    [Test]
    public void Select_WhereWithParam_PopulatesBindings()
    {
        var p = new Param<int>("userId");
        var q = Select.From("users")
            .Where(new ColumnRef<int>("id") == p, p.Bind(42))
            .Build();

        Assert.That(q.Sql, Does.Contain("WHERE id = @userId"));
        Assert.That(q.Bindings["userId"], Is.EqualTo(42));
    }

    [Test]
    public void Select_OrderByDesc_EmitsDescKeyword()
    {
        var q = Select.From("events").OrderBy("created_at", SortDirection.Descending).Build();
        Assert.That(q.Sql, Does.Contain("ORDER BY created_at DESC"));
    }

    [Test]
    public void Select_LimitAndOffset_AppendedCorrectly()
    {
        var q = Select.From("items").Limit(10).Offset(20).Build();
        Assert.That(q.Sql, Does.EndWith("LIMIT 10 OFFSET 20"));
    }

    [Test]
    public void Select_InnerJoin_EmitsJoinClause()
    {
        var q = Select.From("orders", "o")
            .InnerJoin("users",
                new ColumnRef<int>("o.user_id").IsEqualTo(new ColumnRef<int>("u.id")),
                "u")
            .Columns("o.id", "u.name")
            .Build();

        Assert.That(q.Sql, Does.Contain("INNER JOIN users AS u ON o.user_id = u.id"));
    }

    [Test]
    public void Select_LeftJoin_EmitsLeftJoinClause()
    {
        var q = Select.From("orders", "o")
            .LeftJoin("addresses",
                new ColumnRef<int>("o.addr_id").IsEqualTo(new ColumnRef<int>("a.id")),
                "a")
            .Build();

        Assert.That(q.Sql, Does.Contain("LEFT JOIN addresses AS a ON o.addr_id = a.id"));
    }

    [Test]
    public void Select_MultipleOrderBy_EmitsCommaSeparated()
    {
        var q = Select.From("t")
            .OrderBy("a")
            .ThenBy("b", SortDirection.Descending)
            .Build();

        Assert.That(q.Sql, Does.Contain("ORDER BY a ASC, b DESC"));
    }

    [Test]
    public void Select_IsImmutable_OriginalUnchanged()
    {
        var original = Select.From("t");
        var withLimit = original.Limit(5);

        Assert.That(original.Build().Sql, Does.Not.Contain("LIMIT"));
        Assert.That(withLimit.Build().Sql, Does.Contain("LIMIT 5"));
    }

    // =========================================================================
    // Insert
    // =========================================================================

    [Test]
    public void Insert_BasicValues_EmitsInsertStatement()
    {
        var q = Insert.Into("users")
            .Set("name", "Alice")
            .Set("age", 30)
            .Build();

        Assert.That(q.Sql, Does.StartWith("INSERT INTO users"));
        Assert.That(q.Sql, Does.Contain("(name, age)"));
        Assert.That(q.Bindings.Count, Is.EqualTo(2));
    }

    [Test]
    public void Insert_OrReplace_EmitsReplaceConflict()
    {
        var q = Insert.Into("users")
            .OnConflict(ConflictResolution.Replace)
            .Set("id", 1)
            .Set("name", "Bob")
            .Build();

        Assert.That(q.Sql, Does.StartWith("INSERT OR REPLACE INTO users"));
    }

    [Test]
    public void Insert_OrIgnore_EmitsIgnoreConflict()
    {
        var q = Insert.Into("users")
            .OnConflict(ConflictResolution.Ignore)
            .Set("id", 1)
            .Build();

        Assert.That(q.Sql, Does.StartWith("INSERT OR IGNORE INTO users"));
    }

    [Test]
    public void Insert_WithNamedParam_IncludesParamInSql()
    {
        var p = new Param<string>("userName");
        var q = Insert.Into("users")
            .Set("name", p)
            .Build();

        // The SQL should reference @userName; the value is supplied at execution time
        Assert.That(q.Sql, Does.Contain("@userName"));
    }

    [Test]
    public void Insert_NoValues_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Insert.Into("t").Build());
    }

    // =========================================================================
    // Update
    // =========================================================================

    [Test]
    public void Update_BasicSet_EmitsUpdateStatement()
    {
        var q = Update.Table("users")
            .Set("name", "Dave")
            .Build();

        Assert.That(q.Sql, Does.StartWith("UPDATE users SET name = @"));
        Assert.That(q.Bindings.Values, Does.Contain("Dave"));
    }

    [Test]
    public void Update_WithWhere_AppendsWhereClause()
    {
        var p = new Param<int>("uid");
        var q = Update.Table("users")
            .Set("name", "Eve")
            .Where(new ColumnRef<int>("id") == p, p.Bind(7))
            .Build();

        Assert.That(q.Sql, Does.Contain("WHERE id = @uid"));
        Assert.That(q.Bindings["uid"], Is.EqualTo(7));
    }

    [Test]
    public void Update_NoSet_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Update.Table("t").Build());
    }

    // =========================================================================
    // DDL — CreateTable
    // =========================================================================

    [Test]
    public void CreateTable_EmitsCreateStatement()
    {
        var q = CreateTable.Named("products")
            .Column("id",    ColumnType.Integer, ColumnConstraint.PrimaryKey, ColumnConstraint.AutoIncrement)
            .Column("name",  ColumnType.Text,    ColumnConstraint.NotNull)
            .Column("price", ColumnType.Real)
            .Build();

        Assert.That(q.Sql, Does.StartWith("CREATE TABLE IF NOT EXISTS products"));
        Assert.That(q.Sql, Does.Contain("id INTEGER PRIMARY KEY AUTOINCREMENT"));
        Assert.That(q.Sql, Does.Contain("name TEXT NOT NULL"));
        Assert.That(q.Sql, Does.Contain("price REAL"));
    }

    [Test]
    public void CreateTable_WithoutIfNotExists_OmitsClause()
    {
        var q = CreateTable.Named("t", ifNotExists: false)
            .Column("x", ColumnType.Integer)
            .Build();

        Assert.That(q.Sql, Does.StartWith("CREATE TABLE t"));
        Assert.That(q.Sql, Does.Not.Contain("IF NOT EXISTS"));
    }

    [Test]
    public void CreateTable_NoColumns_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CreateTable.Named("t").Build());
    }

    [Test]
    public void CreateTable_ForeignKey_EmitsReferences()
    {
        var q = CreateTable.Named("orders")
            .Column("id",      ColumnType.Integer, ColumnConstraint.PrimaryKey)
            .Column("user_id", ColumnType.Integer, ColumnConstraint.References("users", "id"))
            .Build();

        Assert.That(q.Sql, Does.Contain("REFERENCES users(id)"));
    }

    [Test]
    public void CreateTable_Default_EmitsDefaultClause()
    {
        var q = CreateTable.Named("t")
            .Column("active", ColumnType.Integer, ColumnConstraint.Default(1))
            .Build();

        Assert.That(q.Sql, Does.Contain("DEFAULT (1)"));
    }

    // =========================================================================
    // DDL — AlterTable
    // =========================================================================

    [Test]
    public void AlterTable_Rename_EmitsRenameStatement()
    {
        var q = AlterTable.Rename("old_name", "new_name").Build();
        Assert.That(q.Sql, Is.EqualTo("ALTER TABLE old_name RENAME TO new_name"));
    }

    [Test]
    public void AlterTable_AddColumn_EmitsAddColumnStatement()
    {
        var q = AlterTable.AddColumn("users", "email", ColumnType.Text, ColumnConstraint.Unique).Build();
        Assert.That(q.Sql, Is.EqualTo("ALTER TABLE users ADD COLUMN email TEXT UNIQUE"));
    }

    [Test]
    public void AlterTable_DropColumn_EmitsDropColumnStatement()
    {
        var q = AlterTable.DropColumn("users", "old_col").Build();
        Assert.That(q.Sql, Is.EqualTo("ALTER TABLE users DROP COLUMN old_col"));
    }

    // =========================================================================
    // DDL — DropTable / CreateIndex / DropIndex
    // =========================================================================

    [Test]
    public void DropTable_IfExists_EmitsIfExistsClause()
    {
        var q = DropTable.Named("old_table").Build();
        Assert.That(q.Sql, Is.EqualTo("DROP TABLE IF EXISTS old_table"));
    }

    [Test]
    public void CreateIndex_Unique_EmitsUniqueIndex()
    {
        var q = CreateIndex.Named("idx_email")
            .On("users")
            .Column("email")
            .Unique()
            .Build();

        Assert.That(q.Sql, Does.StartWith("CREATE UNIQUE INDEX IF NOT EXISTS idx_email ON users"));
        Assert.That(q.Sql, Does.Contain("(email)"));
    }

    [Test]
    public void CreateIndex_MultiColumn_EmitsCommaList()
    {
        var q = CreateIndex.Named("idx_name_age")
            .On("users")
            .Column("last_name")
            .Column("first_name")
            .Build();

        Assert.That(q.Sql, Does.Contain("(last_name, first_name)"));
    }

    [Test]
    public void DropIndex_EmitsDropIndexStatement()
    {
        var q = DropIndex.Named("idx_email").Build();
        Assert.That(q.Sql, Is.EqualTo("DROP INDEX IF EXISTS idx_email"));
    }
}
