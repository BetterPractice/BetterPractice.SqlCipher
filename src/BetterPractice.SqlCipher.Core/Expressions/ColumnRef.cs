namespace BetterPractice.SqlCipher;

/// <summary>
/// A typed reference to a named column. Produces expression nodes via operator overloads.
/// Instances for entity types are emitted by the source generator in companion Columns classes.
/// May also be constructed directly for use with the query builder.
/// </summary>
public sealed class ColumnRef<T>
{
    public ColumnRef(string columnName)
    {
        ArgumentException.ThrowIfNullOrEmpty(columnName);
        ColumnName = columnName;
    }

    /// <summary>The SQL column name as it appears in the database.</summary>
    public string ColumnName { get; }

    // -------------------------------------------------------------------------
    // Comparison operators — value RHS
    // -------------------------------------------------------------------------

    public static Expression operator ==(ColumnRef<T> col, T? value) =>
        value is null
            ? new IsNullExpression(col.ColumnName, isNull: true)
            : new CompareExpression(col.ColumnName, ComparisonOperator.Equal, value, false, null);

    public static Expression operator !=(ColumnRef<T> col, T? value) =>
        value is null
            ? new IsNullExpression(col.ColumnName, isNull: false)
            : new CompareExpression(col.ColumnName, ComparisonOperator.NotEqual, value, false, null);

    public static CompareExpression operator <(ColumnRef<T> col, T value) =>
        new(col.ColumnName, ComparisonOperator.LessThan, value, false, null);

    public static CompareExpression operator <=(ColumnRef<T> col, T value) =>
        new(col.ColumnName, ComparisonOperator.LessThanOrEqual, value, false, null);

    public static CompareExpression operator >(ColumnRef<T> col, T value) =>
        new(col.ColumnName, ComparisonOperator.GreaterThan, value, false, null);

    public static CompareExpression operator >=(ColumnRef<T> col, T value) =>
        new(col.ColumnName, ComparisonOperator.GreaterThanOrEqual, value, false, null);

    // -------------------------------------------------------------------------
    // Comparison operators — Param<T> RHS
    // -------------------------------------------------------------------------

    public static CompareExpression operator ==(ColumnRef<T> col, Param<T> param) =>
        new(col.ColumnName, ComparisonOperator.Equal, null, true, param.Name);

    public static CompareExpression operator !=(ColumnRef<T> col, Param<T> param) =>
        new(col.ColumnName, ComparisonOperator.NotEqual, null, true, param.Name);

    public static CompareExpression operator <(ColumnRef<T> col, Param<T> param) =>
        new(col.ColumnName, ComparisonOperator.LessThan, null, true, param.Name);

    public static CompareExpression operator <=(ColumnRef<T> col, Param<T> param) =>
        new(col.ColumnName, ComparisonOperator.LessThanOrEqual, null, true, param.Name);

    public static CompareExpression operator >(ColumnRef<T> col, Param<T> param) =>
        new(col.ColumnName, ComparisonOperator.GreaterThan, null, true, param.Name);

    public static CompareExpression operator >=(ColumnRef<T> col, Param<T> param) =>
        new(col.ColumnName, ComparisonOperator.GreaterThanOrEqual, null, true, param.Name);

    // -------------------------------------------------------------------------
    // Factory methods
    // -------------------------------------------------------------------------

    public IsNullExpression IsNull() => new(ColumnName, isNull: true);
    public IsNullExpression IsNotNull() => new(ColumnName, isNull: false);

    public BetweenExpression Between(T lower, T upper) =>
        new(ColumnName, lower!, upper!);

    public InExpression In(params T[] values) =>
        new(ColumnName, values.Cast<object>().ToList());

    public LikeExpression Like(string pattern) => new(ColumnName, pattern);

    /// <summary>Column-to-column equality comparison, for JOIN ON conditions.</summary>
    public ColumnCompareExpression IsEqualTo(ColumnRef<T> other) =>
        new(ColumnName, ComparisonOperator.Equal, other.ColumnName);

    /// <summary>Column-to-column inequality comparison, for JOIN ON conditions.</summary>
    public ColumnCompareExpression IsNotEqualTo(ColumnRef<T> other) =>
        new(ColumnName, ComparisonOperator.NotEqual, other.ColumnName);

    // Required when overloading == and !=
    public override bool Equals(object? obj) => ReferenceEquals(this, obj);
    public override int GetHashCode() => ColumnName.GetHashCode(StringComparison.Ordinal);
}

/// <summary>
/// Provides &amp;, |, and ! logical composition operators on expression types.
/// Because C# does not allow operators on interfaces, these are defined on each
/// concrete expression type and unified via this non-generic base.
/// Callers can use .And(), .Or(), .Not() extension methods as a readable alternative.
/// </summary>
public abstract class Expression : IExpression
{
    public static AndExpression operator &(Expression left, Expression right)  => new(left, right);
    public static AndExpression operator &(Expression left, IExpression right) => new(left, right);
    public static AndExpression operator &(IExpression left, Expression right) => new(left, right);
    public static OrExpression  operator |(Expression left, Expression right)  => new(left, right);
    public static OrExpression  operator |(Expression left, IExpression right) => new(left, right);
    public static OrExpression  operator |(IExpression left, Expression right) => new(left, right);
    public static NotExpression operator !(Expression operand)                 => new(operand);
}
