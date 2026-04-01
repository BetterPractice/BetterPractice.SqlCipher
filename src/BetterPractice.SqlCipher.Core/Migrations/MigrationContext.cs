using BetterPractice.SqlCipher.Internal;

namespace BetterPractice.SqlCipher;

/// <summary>
/// Provides database operations scoped to a migration's transaction.
/// Provided to <see cref="IMigration.Up"/> and <see cref="IMigration.Down"/>.
/// Must not be stored or used outside the migration method body.
/// </summary>
public sealed class MigrationContext
{
    private readonly DatabaseConnection _connection;

    internal MigrationContext(DatabaseConnection connection)
    {
        _connection = connection;
    }

    /// <summary>Executes raw SQL. Returns rows affected.</summary>
    public int Execute(string sql, IReadOnlyDictionary<string, object?>? parameters = null) =>
        _connection.Execute(sql, parameters);

    /// <summary>Executes raw SQL and returns a fully materialized ResultSet.</summary>
    public ResultSet Query(string sql, IReadOnlyDictionary<string, object?>? parameters = null) =>
        _connection.Query(sql, parameters);
}
