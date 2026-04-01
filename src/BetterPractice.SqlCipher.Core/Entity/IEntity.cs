namespace BetterPractice.SqlCipher;

/// <summary>
/// Marker interface for entity types. Implement this on any class or struct
/// that should be mapped to a database table by the source generator.
/// </summary>
/// <typeparam name="TId">Primary key type. Supported: int, long, string, Guid.</typeparam>
public interface IEntity<TId> where TId : notnull { }
