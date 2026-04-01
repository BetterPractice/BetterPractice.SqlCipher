namespace BetterPractice.SqlCipher.Internal;

/// <summary>
/// A unit of work submitted to the Operation Queue for execution against a database.
/// Not part of the public API — callers interact through DatabaseHandle.
/// </summary>
internal abstract class Operation
{
    /// <summary>
    /// Executes this operation against the open database connection.
    /// Called exclusively by the Operation Queue background task.
    /// Must set a result or exception on any internal TaskCompletionSource before returning.
    /// </summary>
    internal abstract void Execute(DatabaseConnection connection);
}

/// <summary>
/// An operation that executes SQL and returns the number of rows affected.
/// </summary>
internal sealed class ExecuteOperation : Operation
{
    private readonly string _sql;
    private readonly IReadOnlyDictionary<string, object?>? _parameters;
    private readonly TaskCompletionSource<int> _tcs;

    internal ExecuteOperation(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters,
        TaskCompletionSource<int> tcs)
    {
        _sql        = sql;
        _parameters = parameters;
        _tcs        = tcs;
    }

    internal override void Execute(DatabaseConnection connection)
    {
        try
        {
            int rows = connection.Execute(_sql, _parameters);
            _tcs.SetResult(rows);
        }
        catch (Exception ex)
        {
            _tcs.SetException(ex);
        }
    }
}

/// <summary>
/// An operation that executes SQL and returns a fully materialized ResultSet.
/// </summary>
internal sealed class QueryOperation : Operation
{
    private readonly string _sql;
    private readonly IReadOnlyDictionary<string, object?>? _parameters;
    private readonly TaskCompletionSource<ResultSet> _tcs;

    internal QueryOperation(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters,
        TaskCompletionSource<ResultSet> tcs)
    {
        _sql        = sql;
        _parameters = parameters;
        _tcs        = tcs;
    }

    internal override void Execute(DatabaseConnection connection)
    {
        try
        {
            ResultSet result = connection.Query(_sql, _parameters);
            _tcs.SetResult(result);
        }
        catch (Exception ex)
        {
            _tcs.SetException(ex);
        }
    }
}

/// <summary>
/// An operation that executes an arbitrary synchronous function against the connection
/// and returns a typed result. Used for transactions, migrations, entity operations,
/// and other composite work.
/// </summary>
internal sealed class CompositeOperation<T> : Operation
{
    private readonly Func<DatabaseConnection, T> _body;
    private readonly TaskCompletionSource<T> _tcs;

    internal CompositeOperation(
        Func<DatabaseConnection, T> body,
        TaskCompletionSource<T> tcs)
    {
        _body = body;
        _tcs  = tcs;
    }

    internal override void Execute(DatabaseConnection connection)
    {
        try
        {
            T result = _body(connection);
            _tcs.SetResult(result);
        }
        catch (Exception ex)
        {
            _tcs.SetException(ex);
        }
    }
}
