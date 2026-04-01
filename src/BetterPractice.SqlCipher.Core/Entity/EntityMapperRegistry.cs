using System.Collections.Concurrent;

namespace BetterPractice.SqlCipher;

/// <summary>
/// Registry of source-generated entity mappers.
/// The source generator registers each mapper via a ModuleInitializer at startup.
/// </summary>
public static class EntityMapperRegistry
{
    private static readonly ConcurrentDictionary<Type, object> _mappers = new();

    /// <summary>
    /// Registers a mapper for the given entity type.
    /// Called automatically by source-generated module initializers — do not call directly.
    /// </summary>
    public static void Register<T, TId>(IEntityMapper<T, TId> mapper)
        where T : IEntity<TId>
        where TId : notnull
    {
        _mappers[typeof(T)] = mapper;
    }

    /// <summary>
    /// Retrieves the mapper for the given entity type.
    /// Throws if no mapper is registered — the source generator did not run
    /// or the entity type is not in a project that references the generator.
    /// </summary>
    internal static IEntityMapper<T, TId> GetMapper<T, TId>()
        where T : IEntity<TId>
        where TId : notnull
    {
        if (_mappers.TryGetValue(typeof(T), out object? mapper))
            return (IEntityMapper<T, TId>)mapper;

        throw new InvalidOperationException(
            $"No entity mapper registered for '{typeof(T).FullName}'. " +
            $"Ensure the type implements IEntity<{typeof(TId).Name}> and is in a project " +
            $"that references BetterPractice.SqlCipher.Generator.");
    }
}
