# Feature: Entity / ORM

> Covers typed entity mapping, save, fetch, and delete operations.
> See `/spec/interfaces/entity.md` for the API contract.
> See ADR 004 (`/spec/decisions/004-entity-mapping.md`) for mapping strategy.
> See ADR 005 (`/spec/decisions/005-expression-system.md`) for expression system.

---

## Overview

The Entity layer provides typed operations over database rows. Entities are plain C# types implementing `IEntity<TId>`. The source generator inspects these types and emits companion mapper types that handle row-to-object and object-to-row conversion without reflection. Entity operations are available on both `DatabaseHandle` and `ITransactionContext`.

---

## Defining an Entity

```csharp
public class User : IEntity<int>
{
    [PrimaryKey]
    public int Id { get; init; }

    public string Name { get; init; }

    [Column("birth_year")]
    public int BirthYear { get; init; }

    public string? Email { get; init; }   // nullable — maps to SQL NULL
}
```

**Rules:**
- Must implement `IEntity<TId>` where `TId` is the primary key type
- Exactly one property must be marked `[PrimaryKey]`
- All properties with a public `get` and a public or `init` set are mapped unless decorated with `[NotMapped]`
- `[Column("name")]` overrides the column name; otherwise the property name is used as-is
- The primary key property must have an `init` or `set` accessor so the generator can write back auto-assigned values
- Complex types (nested objects, collections) are not supported in this version — use raw SQL or the query builder for those

**Valid primary key types:** `int`, `long`, `string`, `Guid`. Auto-assignment (reading `last_insert_rowid()`) applies only to `int` and `long` PKs when the value is `0` or `default`. String and Guid PKs are always caller-assigned.

---

## Save (Insert / Upsert)

**Trigger:** `DatabaseHandle.Save<T>(T entity)` or `ITransactionContext.Save<T>(T entity)`

**Behavior:**
- If the PK is `int` or `long` and equals `0`: plain `INSERT` omitting the PK column, then reads `last_insert_rowid()` and returns a new entity instance with the assigned ID
- Otherwise: `INSERT INTO ... ON CONFLICT(<pkColumn>) DO UPDATE SET col = excluded.col, ...` (upsert)
- Special case: if the entity has only a PK column and no other mapped columns: `INSERT OR IGNORE`

**Returns:** A new instance of `T` with all fields as stored (including the assigned PK for auto-increment cases). The original instance is not mutated.

**Batch save:** `Save<T>(IReadOnlyList<T> entities)` wraps all inserts in a single transaction. Returns `IReadOnlyList<T>` with assigned PKs. The batch runs as a single Operation on the queue.

---

## Fetch

**Trigger:** `DatabaseHandle.Fetch<T>()`, `Fetch<T>(IExpression where, params ParamBinding[] bindings)`, `FetchOne<T>(TId id)`

**Behavior:**
- `Fetch<T>()` — `SELECT * FROM <table>`
- `Fetch<T>(IExpression where, ...)` — `SELECT * FROM <table> WHERE <rendered expression>`
- `FetchOne<T>(TId id)` — `SELECT * FROM <table> WHERE <pk> = @id LIMIT 1`, returns `null` if not found

All results are fully materialized before returning. Row-to-object conversion is performed by the generated companion mapper.

**Column mapping:** The companion mapper reads columns by name (not position). Extra columns in the result set that have no corresponding property are ignored. A column named in the entity but absent from the result set causes a `MappingException`.

---

## Delete

**Trigger:** `DatabaseHandle.Delete<T>(TId id)` or `ITransactionContext.Delete<T>(TId id)`

**Behavior:** `DELETE FROM <table> WHERE <pk> = @id`

**Returns:** `int` — number of rows deleted (0 or 1 for a single-record delete by PK).

**Bulk delete by expression:**
`DatabaseHandle.Delete<T>(IExpression where, params ParamBinding[] bindings)`
`DELETE FROM <table> WHERE <rendered expression>`
Returns the count of deleted rows.

---

## Source Generator: Companion Mapper

For each entity type, the generator emits a companion mapper (internal, not part of the public API):

```csharp
internal sealed class UserEntityMapper : IEntityMapper<User, int>
{
    public static UserEntityMapper Instance { get; } = new();

    public string TableName => "users";         // from [Table("users")] or entity class name lowercased
    public string PrimaryKeyColumn => "id";

    public User FromRow(Row row) { ... }        // generated property-by-property assignment
    public IReadOnlyList<(string, object?)> ToColumns(User entity) { ... }  // all mapped columns
    public int GetPrimaryKey(User entity) => entity.Id;
    public User WithPrimaryKey(User entity, int id) => entity with { Id = id };
}
```

`WithPrimaryKey` uses C# `with` expression for `record` types or calls the constructor/init properties for regular classes. The generator emits whichever form is appropriate for the entity type.

`[Table("name")]` on the entity class overrides the table name. Without it, the table name defaults to the class name as-is (no automatic pluralization or case conversion).

---

## `ComplexColumnStrategy`

Properties of types not directly mappable to a SQLite type (`int`, `long`, `double`, `string`, `bool`, `byte[]`, `Guid`, `DateTime`, nullable versions of all the above) require a `ComplexColumnStrategy` to serialize to a single column value.

The default strategy is JSON (the property value is JSON-serialized into a TEXT column). A custom strategy can be supplied per-property via `[ComplexColumn(typeof(MyStrategy))]`.

Complex column support is provided at the property level — the column stores a single scalar value. Arrays, collections, and nested objects are serialized as a unit. There is no multi-column decomposition.

---

## `DateTime` and `Guid` Mapping

| C# type | SQLite column type | Stored as |
|---|---|---|
| `DateTime` | TEXT | ISO-8601 UTC string (`yyyy-MM-ddTHH:mm:ss.fffffffZ`) |
| `DateTimeOffset` | TEXT | ISO-8601 with offset |
| `Guid` | TEXT | `ToString("D")` (lowercase, hyphenated) |
| `bool` | INTEGER | 0 or 1 |
| `byte[]` | BLOB | raw bytes |
| `enum` (integer-backed) | INTEGER | underlying integer value |
| `enum` (string-backed) | TEXT | `.ToString()` |

---

## Limitations (This Version)

- No relationship loading (no navigation properties, no joins via Entity API)
- No `SELECT` column projection — always `SELECT *`
- No `ORDER BY`, `LIMIT`, or `OFFSET` on `Fetch` — use raw SQL or the query builder for sorted/paged queries
- No composite primary keys
- No automatic pluralization of table names
- Delete by entity instance (`Delete<T>(T entity)`) is not provided — delete by ID or expression only
