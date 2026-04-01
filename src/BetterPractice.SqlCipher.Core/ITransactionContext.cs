using BetterPractice.SqlCipher.Internal;

namespace BetterPractice.SqlCipher;

/// <summary>
/// Provides inline database operations within an active transaction.
/// Provided to the body callback of <see cref="DatabaseHandle.ExecuteTransaction"/>.
/// Must not be stored or used outside the callback — there is no compile-time enforcement of this.
/// </summary>
public interface ITransactionContext
{
    /// <summary>Executes raw SQL. Returns rows affected.</summary>
    int Execute(string sql, IReadOnlyDictionary<string, object?>? parameters = null);

    /// <summary>Executes raw SQL and returns a fully materialized ResultSet.</summary>
    ResultSet Query(string sql, IReadOnlyDictionary<string, object?>? parameters = null);
}

/// <summary>
/// Internal implementation of ITransactionContext backed by a DatabaseConnection.
/// </summary>
internal sealed class TransactionContext : ITransactionContext
{
    private readonly DatabaseConnection _connection;

    internal TransactionContext(DatabaseConnection connection)
    {
        _connection = connection;
    }

    public int Execute(string sql, IReadOnlyDictionary<string, object?>? parameters = null) =>
        _connection.Execute(sql, parameters);

    public ResultSet Query(string sql, IReadOnlyDictionary<string, object?>? parameters = null) =>
        _connection.Query(sql, parameters);
}
