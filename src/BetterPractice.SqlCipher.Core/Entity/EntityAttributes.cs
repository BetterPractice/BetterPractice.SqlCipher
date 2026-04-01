namespace BetterPractice.SqlCipher;

/// <summary>Marks a property as the primary key. Exactly one per entity type.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PrimaryKeyAttribute : Attribute { }

/// <summary>
/// Overrides the column name used in SQL.
/// Without this, the property name is used as-is.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ColumnAttribute : Attribute
{
    public ColumnAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    public string Name { get; }
}

/// <summary>
/// Overrides the table name for an entity.
/// Without this, the entity class name is used as-is.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class TableAttribute : Attribute
{
    public TableAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    public string Name { get; }
}

/// <summary>Excludes a property from column mapping.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotMappedAttribute : Attribute { }

/// <summary>
/// Specifies a ComplexColumnStrategy type for serializing a property to a single column value.
/// The strategy type must implement <see cref="IComplexColumnStrategy"/> and have a public
/// parameterless constructor.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ComplexColumnAttribute : Attribute
{
    public ComplexColumnAttribute(Type strategyType)
    {
        ArgumentNullException.ThrowIfNull(strategyType);
        StrategyType = strategyType;
    }

    public Type StrategyType { get; }
}
