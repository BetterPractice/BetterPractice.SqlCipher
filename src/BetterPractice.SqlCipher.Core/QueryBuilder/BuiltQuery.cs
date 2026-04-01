namespace BetterPractice.SqlCipher;

/// <summary>
/// The output of a query builder's Build() method.
/// Contains a template-stable SQL string and named parameter bindings.
/// </summary>
public sealed class BuiltQuery
{
    internal BuiltQuery(string sql, IReadOnlyDictionary<string, object?> bindings)
    {
        Sql      = sql;
        Bindings = bindings;
    }

    public string Sql { get; }
    public IReadOnlyDictionary<string, object?> Bindings { get; }
}
