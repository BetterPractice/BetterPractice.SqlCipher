namespace BetterPractice.SqlCipher;

/// <summary>
/// Interface implemented by source-generated companion mapper types.
/// Not intended for direct use — use the typed extension methods emitted by the generator.
/// </summary>
public interface IEntityMapper<T, TId>
    where T : IEntity<TId>
    where TId : notnull
{
    string TableName { get; }
    string PrimaryKeyColumn { get; }

    T Save(ISyncContext ctx, T entity);

    IReadOnlyList<T> FetchAll(ISyncContext ctx);

    /// <param name="whereSql">Pre-rendered WHERE clause SQL fragment (no "WHERE" keyword).</param>
    /// <param name="parameters">Named parameters for the WHERE clause.</param>
    IReadOnlyList<T> FetchWhere(ISyncContext ctx, string whereSql, IReadOnlyDictionary<string, object?> parameters);

    T? FetchOne(ISyncContext ctx, TId id);

    int Delete(ISyncContext ctx, TId id);

    /// <param name="whereSql">Pre-rendered WHERE clause SQL fragment (no "WHERE" keyword).</param>
    /// <param name="parameters">Named parameters for the WHERE clause.</param>
    int DeleteWhere(ISyncContext ctx, string whereSql, IReadOnlyDictionary<string, object?> parameters);
}
