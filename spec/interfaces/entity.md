# Interface: Entity Types

> Type contracts for the Entity / ORM layer.
> See `/spec/features/entity-orm.md` for behavioral specification.
> See ADR 004 (`/spec/decisions/004-entity-mapping.md`) for mapping strategy.

---

## `IEntity<TId>`

Marker interface. All entity types must implement this interface to be recognized by the source generator and entity operations.

```csharp
public interface IEntity<TId>
    where TId : notnull
{
}
```

`TId` is constrained to non-nullable types. Valid values: `int`, `long`, `string`, `Guid`.

---

## Attributes

```csharp
/// <summary>Marks a property as the primary key. Exactly one per entity type.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PrimaryKeyAttribute : Attribute { }

/// <summary>Overrides the column name used in SQL. Without this, the property name is used as-is.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ColumnAttribute : Attribute
{
    public string Name { get; }
    public ColumnAttribute(string name);
}

/// <summary>Overrides the table name. Without this, the entity class name is used as-is.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TableAttribute : Attribute
{
    public string Name { get; }
    public TableAttribute(string name);
}

/// <summary>Excludes a property from column mapping.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotMappedAttribute : Attribute { }

/// <summary>
/// Specifies a ComplexColumnStrategy type to use for this property.
/// The strategy type must implement IComplexColumnStrategy and have a public parameterless constructor.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ComplexColumnAttribute : Attribute
{
    public Type StrategyType { get; }
    public ComplexColumnAttribute(Type strategyType);
}
```

---

## `IEntityMapper<T, TId>`

Internal interface implemented by source-generated companion mappers. Not part of the public API — never used directly by callers.

```csharp
internal interface IEntityMapper<T, TId>
    where T : IEntity<TId>
    where TId : notnull
{
    string TableName { get; }
    string PrimaryKeyColumn { get; }

    T FromRow(Row row);
    IReadOnlyList<(string ColumnName, object? Value)> ToColumns(T entity);
    TId GetPrimaryKey(T entity);
    T WithPrimaryKey(T entity, TId id);
}
```

---

## `IComplexColumnStrategy`

Implemented by types that provide custom serialization for complex property values.

```csharp
public interface IComplexColumnStrategy
{
    /// <summary>Serialize a value to a SQLite Value for storage.</summary>
    Value Encode(object value);

    /// <summary>Deserialize a SQLite Value back to the target type.</summary>
    object Decode(Value sqlValue, Type targetType);
}
```

The default implementation (`JsonComplexColumnStrategy`) JSON-serializes to TEXT using `System.Text.Json`. Custom implementations must have a public parameterless constructor (required by the `[ComplexColumn]` attribute instantiation).

---

## `MappingException`

Thrown when row-to-entity conversion fails — e.g., a required column is absent from the result set, or a value cannot be converted to the declared property type.

```csharp
public sealed class MappingException : Exception
{
    public string EntityTypeName { get; }
    public string ColumnName { get; }
}
```

---

## Entity Operations on `DatabaseHandle`

These internal generic methods are not called directly by consumers — the source generator emits typed extension methods per entity that forward to these with both type parameters resolved. See ADR 004 for the full call site design.

```csharp
// Internal — do not call directly
Task<T> Save<T, TId>(T entity) where T : IEntity<TId> where TId : notnull;
Task<IReadOnlyList<T>> Save<T, TId>(IReadOnlyList<T> entities) where T : IEntity<TId> where TId : notnull;
Task<IReadOnlyList<T>> Fetch<T, TId>() where T : IEntity<TId> where TId : notnull;
Task<IReadOnlyList<T>> Fetch<T, TId>(IExpression where, params ParamBinding[] bindings) where T : IEntity<TId> where TId : notnull;
Task<T?> FetchOne<T, TId>(TId id) where T : IEntity<TId> where TId : notnull;
Task<int> Delete<T, TId>(TId id) where T : IEntity<TId> where TId : notnull;
Task<int> Delete<T, TId>(IExpression where, params ParamBinding[] bindings) where T : IEntity<TId> where TId : notnull;
```

### Generated Extension Methods (per entity type)

For each entity `T : IEntity<TId>`, the generator emits extension methods on both `DatabaseHandle` and `ITransactionContext`:

```csharp
// Example for User : IEntity<int>
public static Task<User> Save(this DatabaseHandle db, User entity);
public static Task<IReadOnlyList<User>> Save(this DatabaseHandle db, IReadOnlyList<User> entities);
public static Task<IReadOnlyList<User>> Fetch(this DatabaseHandle db);
public static Task<IReadOnlyList<User>> Fetch(this DatabaseHandle db, IExpression where, params ParamBinding[] bindings);
public static Task<User?> FetchOne(this DatabaseHandle db, int id);
public static Task<int> Delete(this DatabaseHandle db, int id);
public static Task<int> Delete(this DatabaseHandle db, IExpression where, params ParamBinding[] bindings);
```

The `ITransactionContext` variants are synchronous (no `Task` wrapper — inline execution within the transaction):

```csharp
public static User Save(this ITransactionContext ctx, User entity);
public static IReadOnlyList<User> Fetch(this ITransactionContext ctx);
public static IReadOnlyList<User> Fetch(this ITransactionContext ctx, IExpression where, params ParamBinding[] bindings);
public static User? FetchOne(this ITransactionContext ctx, int id);
public static int Delete(this ITransactionContext ctx, int id);
public static int Delete(this ITransactionContext ctx, IExpression where, params ParamBinding[] bindings);
```

**Note:** `Fetch()` with no arguments has no value from which to infer `T`. Consumers call the no-argument overload as `db.Fetch<User>()` — the extension method encodes the entity type, so this is a single explicit type parameter at the call site, not two. All other overloads infer `T` from the argument.

---

## Source Generator Contract

The generator emits one companion mapper per entity type. It is triggered by types implementing `IEntity<TId>`. It validates at compile time:

| Condition | Compile error |
|---|---|
| No `[PrimaryKey]` property | Yes |
| More than one `[PrimaryKey]` property | Yes |
| `[PrimaryKey]` on a property with no `set` or `init` accessor | Yes |
| `TId` type not in the supported set (`int`, `long`, `string`, `Guid`) | Yes |
| Mapped property has no `get` accessor | Yes |
| `[ComplexColumn]` strategy type does not implement `IComplexColumnStrategy` | Yes |
| `[ComplexColumn]` strategy type has no public parameterless constructor | Yes |
