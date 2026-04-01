namespace BetterPractice.SqlCipher.Tests;

/// <summary>
/// Tests the expression system via Select.Where().Build() — the public integration path.
/// No database connection needed.
/// </summary>
[TestFixture]
public sealed class ExpressionTests
{
    // Convenience: render an expression through a Select build
    private static (string Sql, IReadOnlyDictionary<string, object?> Bindings) Build(IExpression expr, params ParamBinding[] bindings) =>
        Select.From("t").Where(expr, bindings).Build() is var q
            ? (q.Sql, q.Bindings)
            : throw new InvalidOperationException();

    private static string Sql(IExpression expr, params ParamBinding[] bindings) =>
        Build(expr, bindings).Sql;

    // =========================================================================
    // Basic comparisons
    // =========================================================================

    [Test]
    public void Compare_Equals_EmitsEqualsSign()
    {
        var sql = Sql(new ColumnRef<int>("age") == 25);
        Assert.That(sql, Does.Contain("WHERE age = @_0"));
    }

    [Test]
    public void Compare_NotEquals_EmitsNotEquals()
    {
        var sql = Sql(new ColumnRef<string>("status") != "active");
        Assert.That(sql, Does.Contain("WHERE status != @_0"));
    }

    [Test]
    public void Compare_LessThan_EmitsLessThan()
    {
        var sql = Sql(new ColumnRef<double>("price") < 9.99);
        Assert.That(sql, Does.Contain("WHERE price < @_0"));
    }

    [Test]
    public void Compare_GreaterThanOrEqual_EmitsGte()
    {
        var sql = Sql(new ColumnRef<int>("score") >= 100);
        Assert.That(sql, Does.Contain("WHERE score >= @_0"));
    }

    [Test]
    public void Compare_WithNamedParam_UsesParamName()
    {
        var p = new Param<int>("minAge");
        var sql = Sql(new ColumnRef<int>("age") >= p, p.Bind(18));
        Assert.That(sql, Does.Contain("WHERE age >= @minAge"));
    }

    // =========================================================================
    // Null checks
    // =========================================================================

    [Test]
    public void Compare_EqualToNull_EmitsIsNull()
    {
        var sql = Sql(new ColumnRef<string?>("email") == (string?)null);
        Assert.That(sql, Does.Contain("WHERE email IS NULL"));
    }

    [Test]
    public void Compare_NotEqualToNull_EmitsIsNotNull()
    {
        var sql = Sql(new ColumnRef<string?>("email") != (string?)null);
        Assert.That(sql, Does.Contain("WHERE email IS NOT NULL"));
    }

    [Test]
    public void IsNull_FactoryMethod_EmitsIsNull()
    {
        var sql = Sql(new ColumnRef<string?>("deleted_at").IsNull());
        Assert.That(sql, Does.Contain("WHERE deleted_at IS NULL"));
    }

    // =========================================================================
    // Compound expressions
    // =========================================================================

    [Test]
    public void And_TwoExpressions_EmitsParenthesised()
    {
        Expression left  = new ColumnRef<int>("age") > 18;
        Expression right = new ColumnRef<string>("status") == "active";
        var (sql, bindings) = Build(left & right);

        Assert.That(sql, Does.Contain("WHERE (age > @_0 AND status = @_1)"));
        Assert.That(bindings["_0"], Is.EqualTo(18));
        Assert.That(bindings["_1"], Is.EqualTo("active"));
    }

    [Test]
    public void Or_TwoExpressions_EmitsParenthesised()
    {
        Expression left  = new ColumnRef<string>("role") == "admin";
        Expression right = new ColumnRef<string>("role") == "moderator";
        var sql = Sql(left | right);
        Assert.That(sql, Does.Contain("WHERE (role = @_0 OR role = @_1)"));
    }

    [Test]
    public void Not_Expression_EmitsNot()
    {
        Expression inner = new ColumnRef<bool>("is_deleted") == true;
        var sql = Sql(!inner);
        Assert.That(sql, Does.Contain("WHERE (NOT is_deleted = @_0)"));
    }

    [Test]
    public void ChainedAndOr_ProducesCorrectGrouping()
    {
        Expression a = new ColumnRef<int>("x") > 0;
        Expression b = new ColumnRef<int>("y") > 0;
        Expression c = new ColumnRef<int>("z") > 0;

        var sql = Sql((a & b) | c);
        Assert.That(sql, Does.Contain("WHERE ((x > @_0 AND y > @_1) OR z > @_2)"));
    }

    // =========================================================================
    // BETWEEN / IN / LIKE
    // =========================================================================

    [Test]
    public void Between_EmitsBetweenClause()
    {
        var (sql, bindings) = Build(new ColumnRef<int>("age").Between(18, 65));
        Assert.That(sql, Does.Contain("WHERE age BETWEEN @_0 AND @_1"));
        Assert.That(bindings["_0"], Is.EqualTo(18));
        Assert.That(bindings["_1"], Is.EqualTo(65));
    }

    [Test]
    public void In_EmitsInClause()
    {
        var (sql, bindings) = Build(new ColumnRef<string>("status").In("active", "pending"));
        Assert.That(sql, Does.Contain("WHERE status IN (@_0, @_1)"));
        Assert.That(bindings.Count, Is.EqualTo(2));
    }

    [Test]
    public void Like_EmitsLikeClause()
    {
        var (sql, bindings) = Build(new ColumnRef<string>("name").Like("Alice%"));
        Assert.That(sql, Does.Contain("WHERE name LIKE @_0"));
        Assert.That(bindings["_0"], Is.EqualTo("Alice%"));
    }

    // =========================================================================
    // Column-to-column comparisons (JOIN ON)
    // =========================================================================

    [Test]
    public void ColumnCompare_TwoColumns_NoParams()
    {
        var q = Select.From("orders", "o")
            .InnerJoin("users",
                new ColumnRef<int>("o.user_id").IsEqualTo(new ColumnRef<int>("u.id")),
                "u")
            .Build();

        Assert.That(q.Sql, Does.Contain("INNER JOIN users AS u ON o.user_id = u.id"));
        Assert.That(q.Bindings, Is.Empty);
    }

    // =========================================================================
    // Named parameter binding
    // =========================================================================

    [Test]
    public void NamedParam_PreBound_AppearsInBindings()
    {
        var p = new Param<int>("userId");
        var (sql, bindings) = Build(new ColumnRef<int>("id") == p, p.Bind(42));

        Assert.That(sql, Does.Contain("WHERE id = @userId"));
        Assert.That(bindings["userId"], Is.EqualTo(42));
    }
}
