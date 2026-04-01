namespace BetterPractice.SqlCipher;

/// <summary>
/// Immutable fluent builder for SELECT statements.
/// Every method returns a new instance — the original is unchanged.
/// </summary>
public sealed class Select
{
    private readonly string _tableName;
    private readonly string? _tableAlias;
    private readonly string[]? _columns;
    private readonly bool _distinct;
    private readonly IExpression? _where;
    private readonly ParamBinding[]? _whereBindings;
    private readonly (string Table, string? Alias, JoinType Type, IExpression On)[] _joins;
    private readonly (string Column, SortDirection Direction)[] _orderBy;
    private readonly int? _limit;
    private readonly int? _offset;

    private Select(
        string tableName,
        string? tableAlias,
        string[]? columns,
        bool distinct,
        IExpression? where,
        ParamBinding[]? whereBindings,
        (string, string?, JoinType, IExpression)[] joins,
        (string, SortDirection)[] orderBy,
        int? limit,
        int? offset)
    {
        _tableName     = tableName;
        _tableAlias    = tableAlias;
        _columns       = columns;
        _distinct      = distinct;
        _where         = where;
        _whereBindings = whereBindings;
        _joins         = joins;
        _orderBy       = orderBy;
        _limit         = limit;
        _offset        = offset;
    }

    public static Select From(string tableName, string? alias = null) =>
        new(tableName, alias, null, false, null, null, [], [], null, null);

    public Select Columns(params string[] columns) =>
        new(_tableName, _tableAlias, columns, _distinct, _where, _whereBindings, _joins, _orderBy, _limit, _offset);

    public Select Distinct() =>
        new(_tableName, _tableAlias, _columns, true, _where, _whereBindings, _joins, _orderBy, _limit, _offset);

    public Select Where(IExpression expression, params ParamBinding[] bindings) =>
        new(_tableName, _tableAlias, _columns, _distinct, expression, bindings, _joins, _orderBy, _limit, _offset);

    public Select InnerJoin(string table, IExpression on, string? alias = null) =>
        AddJoin(table, alias, JoinType.Inner, on);

    public Select LeftJoin(string table, IExpression on, string? alias = null) =>
        AddJoin(table, alias, JoinType.Left, on);

    public Select OrderBy(string column, SortDirection direction = SortDirection.Ascending)
    {
        var newOrder = _orderBy.Append((column, direction)).ToArray();
        return new(_tableName, _tableAlias, _columns, _distinct, _where, _whereBindings, _joins, newOrder, _limit, _offset);
    }

    public Select ThenBy(string column, SortDirection direction = SortDirection.Ascending) =>
        OrderBy(column, direction);

    public Select Limit(int count) =>
        new(_tableName, _tableAlias, _columns, _distinct, _where, _whereBindings, _joins, _orderBy, count, _offset);

    public Select Offset(int skip) =>
        new(_tableName, _tableAlias, _columns, _distinct, _where, _whereBindings, _joins, _orderBy, _limit, skip);

    public BuiltQuery Build()
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);

        // Pre-populate named bindings from WHERE
        if (_whereBindings is { Length: > 0 })
            foreach (var b in _whereBindings)
                parameters[b.Name] = b.Value;

        var sb = new System.Text.StringBuilder();

        sb.Append("SELECT ");
        if (_distinct) sb.Append("DISTINCT ");

        if (_columns is { Length: > 0 })
            sb.Append(string.Join(", ", _columns));
        else
            sb.Append('*');

        string tableRef = _tableAlias is not null ? $"{_tableName} AS {_tableAlias}" : _tableName;
        sb.Append($" FROM {tableRef}");

        foreach (var (table, alias, type, on) in _joins)
        {
            string joinKeyword = type == JoinType.Inner ? "INNER JOIN" : "LEFT JOIN";
            string joinRef = alias is not null ? $"{table} AS {alias}" : table;
            var (onSql, onParams) = ExpressionRenderer.Render(on);
            foreach (var p in onParams) parameters[p.Key] = p.Value;
            sb.Append($" {joinKeyword} {joinRef} ON {onSql}");
        }

        if (_where is not null)
        {
            var (whereSql, whereParams) = ExpressionRenderer.Render(_where, _whereBindings);
            // Merge params (named bindings already pre-loaded; auto-named ones added here)
            foreach (var p in whereParams)
                parameters.TryAdd(p.Key, p.Value);
            sb.Append($" WHERE {whereSql}");
        }

        if (_orderBy.Length > 0)
        {
            var orderClauses = _orderBy.Select(o =>
                $"{o.Column} {(o.Direction == SortDirection.Descending ? "DESC" : "ASC")}");
            sb.Append($" ORDER BY {string.Join(", ", orderClauses)}");
        }

        if (_limit.HasValue) sb.Append($" LIMIT {_limit.Value}");
        if (_offset.HasValue) sb.Append($" OFFSET {_offset.Value}");

        return new BuiltQuery(sb.ToString(), parameters);
    }

    private Select AddJoin(string table, string? alias, JoinType type, IExpression on)
    {
        var newJoins = _joins.Append((table, alias, type, on)).ToArray();
        return new(_tableName, _tableAlias, _columns, _distinct, _where, _whereBindings, newJoins, _orderBy, _limit, _offset);
    }

    private enum JoinType { Inner, Left }
}
