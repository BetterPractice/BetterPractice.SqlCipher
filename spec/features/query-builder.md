# Feature: Query Builder

> Covers the fluent SQL query builder for SELECT, INSERT, UPDATE, and DDL.
> See `/spec/interfaces/expression-types.md` for expression types used in WHERE clauses.

---

## Overview

The query builder provides a fluent, composable API for constructing SQL statements without string concatenation. Builder types are immutable — every fluent method returns a new instance. Builders produce a `BuiltQuery` (SQL string + parameter bindings) that is passed to `DatabaseHandle.Execute` or `DatabaseHandle.Query`.

The SQL string produced by a builder is template-stable: literal values are assigned auto-named parameters so the SQL string is identical across calls with different values. This enables statement cache hits.

---

## `BuiltQuery`

The output of any builder's `Build()` method.

```csharp
public sealed class BuiltQuery
{
    public string Sql { get; }
    public IReadOnlyDictionary<string, object?> Bindings { get; }
}
```

---

## Statement Cache

The statement cache is an LRU cache keyed on the SQL string. Default capacity: 64 entries. Statements are reset and re-bound between uses rather than finalized and re-prepared. The cache is internal and not configurable in this version.

**DDL exclusion:** `CREATE TABLE`, `ALTER TABLE`, `DROP TABLE`, `CREATE INDEX`, `DROP INDEX`, and `PRAGMA` statements are never cached. They are always prepared fresh, executed, and finalized.

**Schema change eviction:** On `SQLITE_SCHEMA` error (schema changed since statement was prepared), the affected statement is evicted from the cache and re-prepared automatically.

---

## `Select`

Immutable fluent builder for SELECT statements.

```csharp
// Construction
Select.From(string tableName)
Select.From(string tableName, string alias)

// Column selection (default is *)
.Columns(params ColumnRef[] columns)   // SELECT specific columns
.Distinct()

// Filtering
.Where(IExpression expression, params ParamBinding[] bindings)

// Joins
.InnerJoin(string table, IExpression on)
.LeftJoin(string table, IExpression on)

// Ordering
.OrderBy(ColumnRef column, SortDirection direction = SortDirection.Ascending)
.ThenBy(ColumnRef column, SortDirection direction = SortDirection.Ascending)

// Paging
.Limit(int count)
.Offset(int skip)

// Terminal
.Build() -> BuiltQuery
```

**Example:**
```csharp
var query = Select.From("users")
    .Where(UserColumns.Age > 18 & UserColumns.Name != null)
    .OrderBy(UserColumns.Name)
    .Limit(10)
    .Build();

var results = await db.Query(query);
```

---

## `Insert`

Immutable fluent builder for INSERT statements.

```csharp
Insert.Into(string tableName)
    .Set(ColumnRef column, object? value)
    .Set(ColumnRef column, Param param)
    .OnConflict(ConflictResolution resolution)   // default: Abort
    .Build() -> BuiltQuery
```

**`ConflictResolution`:**
```csharp
public enum ConflictResolution { Rollback, Abort, Fail, Ignore, Replace }
```

---

## `Update`

Immutable fluent builder for UPDATE statements.

```csharp
Update.Table(string tableName)
    .Set(ColumnRef column, object? value)
    .Set(ColumnRef column, Param param)
    .Where(IExpression expression, params ParamBinding[] bindings)
    .Build() -> BuiltQuery
```

An `Update` without a `.Where()` clause updates all rows. This is permitted — the caller is responsible for supplying a WHERE clause when needed.

---

## DDL Builders

DDL builders produce `BuiltQuery` with no bound parameters — DDL values are rendered inline. DDL statements are never cached.

### `CreateTable`

```csharp
CreateTable.Named(string tableName)
    .IfNotExists()                                   // default: true
    .Column(string name, ColumnType type, params ColumnConstraint[] constraints)
    .Build() -> BuiltQuery
```

**`ColumnType`:** `Integer`, `Real`, `Text`, `Blob`

**`ColumnConstraint`:**
```csharp
ColumnConstraint.PrimaryKey
ColumnConstraint.AutoIncrement      // only valid with PrimaryKey + Integer
ColumnConstraint.NotNull
ColumnConstraint.Unique
ColumnConstraint.Default(object value)
ColumnConstraint.References(string table, string column)
```

No composite primary keys or multi-column UNIQUE constraints at the table level — use raw SQL for those.

### `AlterTable`

Each `AlterTable` instance represents exactly one alteration.

```csharp
AlterTable.Rename(string tableName, string newName)
AlterTable.RenameColumn(string tableName, string columnName, string newName)
AlterTable.AddColumn(string tableName, string columnName, ColumnType type, params ColumnConstraint[] constraints)
AlterTable.DropColumn(string tableName, string columnName)

// Terminal
.Build() -> BuiltQuery
```

### `DropTable`

```csharp
DropTable.Named(string tableName)
    .IfExists()    // default: true
    .Build() -> BuiltQuery
```

### `CreateIndex`

```csharp
CreateIndex.Named(string indexName)
    .On(string tableName)
    .Column(string columnName, SortDirection direction = SortDirection.Ascending)
    .Unique()
    .IfNotExists()    // default: true
    .Build() -> BuiltQuery
```

### `DropIndex`

```csharp
DropIndex.Named(string indexName)
    .IfExists()    // default: true
    .Build() -> BuiltQuery
```

---

## Using Builders with `DatabaseHandle`

Builders produce `BuiltQuery`. The existing `Execute` and `Query` overloads accept `BuiltQuery` directly:

```csharp
Task<int> Execute(BuiltQuery query);
Task<ResultSet> Query(BuiltQuery query);
```

DDL builders are also accepted directly (they bypass the statement cache internally):

```csharp
Task Execute(CreateTable statement);
Task Execute(AlterTable statement);
Task Execute(DropTable statement);
Task Execute(CreateIndex statement);
Task Execute(DropIndex statement);
```

---

## `SortDirection`

```csharp
public enum SortDirection { Ascending, Descending }
```

---

## Limitations (This Version)

- No CTEs (`WITH` / `WITH RECURSIVE`)
- No subqueries
- No `GROUP BY` / `HAVING`
- No `UNION` / `INTERSECT` / `EXCEPT`
- No multi-row `INSERT` (insert multiple rows in one statement) — use `ITransactionContext` with batched single inserts
- No `INSERT ... SELECT`
- No `DELETE` builder — use raw SQL for DELETE, or entity `Delete<T>` for typed deletes
