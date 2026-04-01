namespace BetterPractice.SqlCipher;

/// <summary>
/// Immutable fluent builder for UPDATE statements.
/// An Update without a Where clause updates all rows — this is permitted.
/// </summary>
public sealed class Update
{
    private readonly string _tableName;
    private readonly (string Column, object? Value, bool IsParam, string? ParamName)[] _sets;
    private readonly IExpression? _where;
    private readonly ParamBinding[]? _whereBindings;

    private Update(
        string tableName,
        (string, object?, bool, string?)[] sets,
        IExpression? where,
        ParamBinding[]? whereBindings)
    {
        _tableName     = tableName;
        _sets          = sets;
        _where         = where;
        _whereBindings = whereBindings;
    }

    public static Update Table(string tableName) =>
        new(tableName, [], null, null);

    public Update Set(string column, object? value) =>
        new(_tableName, _sets.Append((column, value, false, null)).ToArray(), _where, _whereBindings);

    public Update Set<T>(string column, Param<T> param) =>
        new(_tableName, _sets.Append((column, null, true, param.Name)).ToArray(), _where, _whereBindings);

    public Update Where(IExpression expression, params ParamBinding[] bindings) =>
        new(_tableName, _sets, expression, bindings);

    public BuiltQuery Build()
    {
        if (_sets.Length == 0)
            throw new InvalidOperationException("Update requires at least one column.");

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        int autoIndex = 0;

        if (_whereBindings is { Length: > 0 })
            foreach (var b in _whereBindings)
                parameters[b.Name] = b.Value;

        var setClauses = new string[_sets.Length];
        for (int i = 0; i < _sets.Length; i++)
        {
            var (col, val, isParam, paramName) = _sets[i];
            if (isParam)
            {
                setClauses[i] = $"{col} = @{paramName}";
            }
            else
            {
                string name = $"_{autoIndex++}";
                parameters[name] = val;
                setClauses[i] = $"{col} = @{name}";
            }
        }

        string sql = $"UPDATE {_tableName} SET {string.Join(", ", setClauses)}";

        if (_where is not null)
        {
            var (whereSql, whereParams) = ExpressionRenderer.Render(_where, _whereBindings);
            foreach (var p in whereParams)
                parameters.TryAdd(p.Key, p.Value);
            sql += $" WHERE {whereSql}";
        }

        return new BuiltQuery(sql, parameters);
    }
}
