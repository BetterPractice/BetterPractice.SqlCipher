namespace BetterPractice.SqlCipher;

/// <summary>A comparison between a column and a literal value or named parameter.</summary>
public sealed class CompareExpression : Expression
{
    internal CompareExpression(string columnName, ComparisonOperator op, object? value, bool isParam, string? paramName)
    {
        ColumnName = columnName;
        Operator   = op;
        Value      = value;
        IsParam    = isParam;
        ParamName  = paramName;
    }

    public string ColumnName { get; }
    public ComparisonOperator Operator { get; }
    public object? Value { get; }
    public bool IsParam { get; }
    public string? ParamName { get; }
}

/// <summary>A comparison between two columns (for JOIN ON conditions).</summary>
public sealed class ColumnCompareExpression : Expression
{
    internal ColumnCompareExpression(string leftColumnName, ComparisonOperator op, string rightColumnName)
    {
        LeftColumnName  = leftColumnName;
        Operator        = op;
        RightColumnName = rightColumnName;
    }

    public string LeftColumnName { get; }
    public ComparisonOperator Operator { get; }
    public string RightColumnName { get; }
}

/// <summary>Logical AND of two expressions.</summary>
public sealed class AndExpression : Expression
{
    internal AndExpression(IExpression left, IExpression right)
    {
        Left  = left;
        Right = right;
    }

    public IExpression Left { get; }
    public IExpression Right { get; }
}

/// <summary>Logical OR of two expressions.</summary>
public sealed class OrExpression : Expression
{
    internal OrExpression(IExpression left, IExpression right)
    {
        Left  = left;
        Right = right;
    }

    public IExpression Left { get; }
    public IExpression Right { get; }
}

/// <summary>Logical NOT of an expression.</summary>
public sealed class NotExpression : Expression
{
    internal NotExpression(IExpression operand) => Operand = operand;

    public IExpression Operand { get; }
}

/// <summary>
/// Extension methods providing &amp;, |, and ! logical composition on IExpression.
/// </summary>
public static class ExpressionExtensions
{
    public static AndExpression And(this IExpression left, IExpression right) => new(left, right);
    public static OrExpression Or(this IExpression left, IExpression right)   => new(left, right);
    public static NotExpression Not(this IExpression operand)                 => new(operand);
}

/// <summary>IS NULL or IS NOT NULL check.</summary>
public sealed class IsNullExpression : Expression
{
    internal IsNullExpression(string columnName, bool isNull)
    {
        ColumnName = columnName;
        IsNull     = isNull;
    }

    public string ColumnName { get; }
    /// <summary>True = IS NULL, False = IS NOT NULL.</summary>
    public bool IsNull { get; }
}

/// <summary>BETWEEN lower AND upper.</summary>
public sealed class BetweenExpression : Expression
{
    internal BetweenExpression(string columnName, object lower, object upper)
    {
        ColumnName = columnName;
        Lower      = lower;
        Upper      = upper;
    }

    public string ColumnName { get; }
    public object Lower { get; }
    public object Upper { get; }
}

/// <summary>IN (...) membership test.</summary>
public sealed class InExpression : Expression
{
    internal InExpression(string columnName, IReadOnlyList<object> values)
    {
        ColumnName = columnName;
        Values     = values;
    }

    public string ColumnName { get; }
    public IReadOnlyList<object> Values { get; }
}

/// <summary>LIKE pattern match.</summary>
public sealed class LikeExpression : Expression
{
    internal LikeExpression(string columnName, string pattern)
    {
        ColumnName = columnName;
        Pattern    = pattern;
    }

    public string ColumnName { get; }
    public string Pattern { get; }
}
