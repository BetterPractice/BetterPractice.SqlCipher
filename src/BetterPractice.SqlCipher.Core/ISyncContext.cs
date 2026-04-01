namespace BetterPractice.SqlCipher;

/// <summary>
/// Synchronous database operations interface used by source-generated entity mappers.
/// Implemented internally by <see cref="Internal.DatabaseConnection"/>.
/// Not intended for direct use outside of generated mapper code.
/// </summary>
public interface ISyncContext
{
    /// <summary>Executes a SQL statement. Returns rows affected.</summary>
    int Execute(string sql, IReadOnlyDictionary<string, object?>? parameters = null);

    /// <summary>Executes a SQL query. Returns a fully materialized result set.</summary>
    ResultSet Query(string sql, IReadOnlyDictionary<string, object?>? parameters = null);
}
