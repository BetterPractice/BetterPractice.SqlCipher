namespace BetterPractice.SqlCipher;

/// <summary>
/// Serializes complex property values (arrays, nested objects, etc.) to and from
/// a single SQLite column value.
/// Implementations must have a public parameterless constructor.
/// </summary>
public interface IComplexColumnStrategy
{
    /// <summary>Serializes a value to a SQLite Value for storage.</summary>
    Value Encode(object value);

    /// <summary>Deserializes a SQLite Value back to the target type.</summary>
    object Decode(Value sqlValue, Type targetType);
}
