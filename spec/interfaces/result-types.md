# Interface: Result Types

> Defines ResultSet, Row, and Value — the types returned by query operations.
> See `/spec/GLOSSARY.md` for term definitions.

---

## ResultSet

An ordered, fully materialized sequence of Rows returned by a query Operation.

```
class ResultSet : IReadOnlyList<Row>
```

**Properties:**

| Property | Type | Description |
|---|---|---|
| `Count` | `int` | Number of rows. Zero if the query returned no rows. |
| `ColumnNames` | `IReadOnlyList<string>` | Ordered list of column names, consistent across all rows. |

**Indexer:** `Row this[int index]`

**Notes:**
- `ResultSet` is fully materialized — the SQLite cursor is read and closed before the result is returned to the caller. There are no lazy-loading or streaming semantics in the public API.
- An empty `ResultSet` (Count == 0) is a valid, non-error result.

---

## Row

A single record within a ResultSet, accessible by column index or column name.

```
class Row : IReadOnlyList<Value>
```

**Indexers:**
- `Value this[int index]` — access by column index (zero-based)
- `Value this[string columnName]` — access by column name

**Properties:**

| Property | Type | Description |
|---|---|---|
| `Count` | `int` | Number of columns in this row. |

**Notes:**
- Column name lookup is case-sensitive, matching SQLite behavior.
- Accessing a column by name that does not exist → explicit exception.

---

## Value

A single typed field within a Row.

```
readonly struct Value
```

**Properties:**

| Property | Type | Description |
|---|---|---|
| `SqliteType` | `SqliteType` | The underlying SQLite storage type of this value. |
| `IsNull` | `bool` | True if `SqliteType` is `Null`. |

**Conversion methods** (each throws if the Value is not convertible to the requested type):

| Method | Return type | Notes |
|---|---|---|
| `AsInt64()` | `long` | SQLite INTEGER |
| `AsDouble()` | `double` | SQLite REAL |
| `AsText()` | `string` | SQLite TEXT |
| `AsBlob()` | `ReadOnlyMemory<byte>` | SQLite BLOB |
| `AsInt32()` | `int` | Checked cast from INTEGER |
| `AsBool()` | `bool` | 0 = false, non-zero = true; requires INTEGER type |

**Notes:**
- `Value` is a value type (`readonly struct`) to avoid heap allocation per field.
- Null is an explicit type, not a missing value. `AsText()` on a NULL Value throws rather than returning null.
- `AsInt32()` throws if the underlying integer value is outside the `int` range.

---

## SqliteType Enumeration

| Value | SQLite affinity |
|---|---|
| `Null` | NULL |
| `Integer` | INTEGER |
| `Real` | REAL |
| `Text` | TEXT |
| `Blob` | BLOB |
