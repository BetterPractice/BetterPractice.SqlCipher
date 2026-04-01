using BetterPractice.SqlCipher.Internal;

namespace BetterPractice.SqlCipher;

/// <summary>
/// Renders an IExpression tree to a parameterized SQL WHERE fragment.
/// Internal — not part of the public API.
/// </summary>
internal static class ExpressionRenderer
{
    internal static (string Sql, Dictionary<string, object?> Parameters) Render(
        IExpression expression, ParamBinding[]? bindings = null)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        int autoIndex = 0;

        // Pre-populate named bindings
        if (bindings is { Length: > 0 })
            foreach (var b in bindings)
                parameters[b.Name] = b.Value;

        string sql = RenderNode(expression, parameters, ref autoIndex);
        return (sql, parameters);
    }

    private static string RenderNode(IExpression expr, Dictionary<string, object?> parameters, ref int autoIndex) =>
        expr switch
        {
            CompareExpression e       => RenderCompare(e, parameters, ref autoIndex),
            ColumnCompareExpression e => RenderColumnCompare(e),
            AndExpression e           => $"({RenderNode(e.Left, parameters, ref autoIndex)} AND {RenderNode(e.Right, parameters, ref autoIndex)})",
            OrExpression e            => $"({RenderNode(e.Left, parameters, ref autoIndex)} OR {RenderNode(e.Right, parameters, ref autoIndex)})",
            NotExpression e           => $"(NOT {RenderNode(e.Operand, parameters, ref autoIndex)})",
            IsNullExpression e        => e.IsNull ? $"{e.ColumnName} IS NULL" : $"{e.ColumnName} IS NOT NULL",
            BetweenExpression e       => RenderBetween(e, parameters, ref autoIndex),
            InExpression e            => RenderIn(e, parameters, ref autoIndex),
            LikeExpression e          => RenderLike(e, parameters, ref autoIndex),
            _ => throw new ArgumentException($"Unknown expression type: {expr.GetType().Name}")
        };

    private static string RenderCompare(CompareExpression e, Dictionary<string, object?> parameters, ref int autoIndex)
    {
        // IsNullExpression is returned by == null / != null — but CompareExpression is only
        // produced for non-null values, so no null check needed here
        string op = e.Operator switch
        {
            ComparisonOperator.Equal              => "=",
            ComparisonOperator.NotEqual           => "!=",
            ComparisonOperator.LessThan           => "<",
            ComparisonOperator.LessThanOrEqual    => "<=",
            ComparisonOperator.GreaterThan        => ">",
            ComparisonOperator.GreaterThanOrEqual => ">=",
            _ => throw new ArgumentException($"Unknown operator: {e.Operator}")
        };

        string paramName = e.IsParam
            ? e.ParamName!
            : AutoName(ref autoIndex, parameters, e.Value);

        return $"{e.ColumnName} {op} @{paramName}";
    }

    private static string RenderColumnCompare(ColumnCompareExpression e)
    {
        string op = e.Operator switch
        {
            ComparisonOperator.Equal    => "=",
            ComparisonOperator.NotEqual => "!=",
            _ => throw new ArgumentException($"Column-to-column comparison only supports = and !=, got: {e.Operator}")
        };
        return $"{e.LeftColumnName} {op} {e.RightColumnName}";
    }

    private static string RenderBetween(BetweenExpression e, Dictionary<string, object?> parameters, ref int autoIndex)
    {
        string lower = AutoName(ref autoIndex, parameters, e.Lower);
        string upper = AutoName(ref autoIndex, parameters, e.Upper);
        return $"{e.ColumnName} BETWEEN @{lower} AND @{upper}";
    }

    private static string RenderIn(InExpression e, Dictionary<string, object?> parameters, ref int autoIndex)
    {
        if (e.Values.Count == 0)
            return "1=0"; // IN () is always false

        var names = new string[e.Values.Count];
        for (int i = 0; i < e.Values.Count; i++)
            names[i] = "@" + AutoName(ref autoIndex, parameters, e.Values[i]);

        return $"{e.ColumnName} IN ({string.Join(", ", names)})";
    }

    private static string RenderLike(LikeExpression e, Dictionary<string, object?> parameters, ref int autoIndex)
    {
        string paramName = AutoName(ref autoIndex, parameters, e.Pattern);
        return $"{e.ColumnName} LIKE @{paramName}";
    }

    private static string AutoName(ref int autoIndex, Dictionary<string, object?> parameters, object? value)
    {
        string name = $"_{autoIndex++}";
        parameters[name] = value;
        return name;
    }
}
