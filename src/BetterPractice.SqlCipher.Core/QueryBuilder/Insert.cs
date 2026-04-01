namespace BetterPractice.SqlCipher;

public enum ConflictResolution { Rollback, Abort, Fail, Ignore, Replace }

/// <summary>
/// Immutable fluent builder for INSERT statements.
/// </summary>
public sealed class Insert
{
    private readonly string _tableName;
    private readonly (string Column, object? Value, bool IsParam, string? ParamName)[] _sets;
    private readonly ConflictResolution _conflict;

    private Insert(
        string tableName,
        (string, object?, bool, string?)[] sets,
        ConflictResolution conflict)
    {
        _tableName = tableName;
        _sets      = sets;
        _conflict  = conflict;
    }

    public static Insert Into(string tableName) =>
        new(tableName, [], ConflictResolution.Abort);

    public Insert Set(string column, object? value) =>
        new(_tableName, _sets.Append((column, value, false, null)).ToArray(), _conflict);

    public Insert Set<T>(string column, Param<T> param) =>
        new(_tableName, _sets.Append((column, null, true, param.Name)).ToArray(), _conflict);

    public Insert OnConflict(ConflictResolution resolution) =>
        new(_tableName, _sets, resolution);

    public BuiltQuery Build()
    {
        if (_sets.Length == 0)
            throw new InvalidOperationException("Insert requires at least one column.");

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        int autoIndex = 0;

        string conflictClause = _conflict switch
        {
            ConflictResolution.Rollback => "OR ROLLBACK ",
            ConflictResolution.Abort    => "",
            ConflictResolution.Fail     => "OR FAIL ",
            ConflictResolution.Ignore   => "OR IGNORE ",
            ConflictResolution.Replace  => "OR REPLACE ",
            _ => ""
        };

        var columns   = new string[_sets.Length];
        var paramRefs = new string[_sets.Length];

        for (int i = 0; i < _sets.Length; i++)
        {
            var (col, val, isParam, paramName) = _sets[i];
            columns[i] = col;
            if (isParam)
            {
                paramRefs[i] = $"@{paramName}";
            }
            else
            {
                string name = $"_{autoIndex++}";
                parameters[name] = val;
                paramRefs[i] = $"@{name}";
            }
        }

        string sql = $"INSERT {conflictClause}INTO {_tableName} " +
                     $"({string.Join(", ", columns)}) " +
                     $"VALUES ({string.Join(", ", paramRefs)})";

        return new BuiltQuery(sql, parameters);
    }
}
