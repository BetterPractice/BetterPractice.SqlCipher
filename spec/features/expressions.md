# Feature: Expression System

> Covers the IExpression type hierarchy, operator overloads, and SQL rendering.
> See `/spec/interfaces/expression-types.md` for the type contracts.
> See ADR 005 (`/spec/decisions/005-expression-system.md`) for rationale.

---

## Overview

The expression system provides a composable, type-safe way to construct SQL WHERE predicates and JOIN conditions without parsing LINQ expression trees. Expressions are built using C# operator overloads on `ColumnRef<T>` and composed with `&` (AND), `|` (OR), and `!` (NOT). The expression tree is rendered to a parameterized SQL fragment at query execution time.

---

## Expression Building

### Comparison Expressions

Produced by overloaded operators on `ColumnRef<T>`:

```csharp
User.Columns.Age > 18          // CompareExpression (GREATER_THAN, literal 18)
User.Columns.Name == "Alice"   // CompareExpression (EQUAL, literal "Alice")
User.Columns.Score != null     // IsNullExpression (IS NOT NULL)
```

When the RHS is `null`, `==` produces `IsNullExpression(IS NULL)` and `!=` produces `IsNullExpression(IS NOT NULL)`. No other comparison operator accepts `null`.

### Logical Composition

```csharp
User.Columns.Age > 18 & User.Columns.Name == "Alice"   // AndExpression
User.Columns.Age < 5  | User.Columns.Age > 95          // OrExpression
!(User.Columns.IsActive == true)                        // NotExpression
```

Operator precedence follows C# rules: `!` binds tighter than `&`, which binds tighter than `|`. Use parentheses to override.

### Factory Methods for Non-Operator Constructs

```csharp
User.Columns.Name.IsNull()
User.Columns.Name.IsNotNull()
User.Columns.Age.Between(18, 65)
User.Columns.Status.In("active", "pending")
User.Columns.Name.Like("Alice%")
```

### Named Parameters

For cases where the same query template is used with varying values:

```csharp
var minAge = new Param<int>("minAge");
IExpression expr = User.Columns.Age > minAge;
db.Fetch<User>(expr, minAge.Bind(18));
```

`Param<T>` is a named placeholder. `Bind(value)` produces a `ParamBinding` (type-erased) passed alongside the expression. Named parameters produce stable SQL templates regardless of value, enabling statement cache hits.

### Cross-Column Comparisons (JOIN ON)

When comparing two columns (e.g., in a JOIN condition), use the `ColumnRef.IsEqualTo(ColumnRef)` factory method:

```csharp
Order.Columns.UserId.IsEqualTo(User.Columns.Id)   // ColumnCompareExpression
```

Operator overloads between two `ColumnRef<T>` instances are not supported (C# cannot distinguish `col == col` from `col == value` without explicit overloads that would conflict). Use `IsEqualTo` and `IsNotEqualTo` for column-to-column comparisons.

---

## SQL Rendering

Expressions are rendered to a SQL fragment and a parameter dictionary by an internal `ExpressionRenderer`. This is not part of the public API.

### Literal Value Auto-Naming

Literal values on the RHS of expressions are assigned auto-names (`_0`, `_1`, ...) during rendering. This keeps the SQL string template-stable across different literal values:

```csharp
User.Columns.Age > 18    // renders: "age > @_0"  bindings: { "_0": 18 }
User.Columns.Age > 25    // renders: "age > @_0"  bindings: { "_0": 25 }
```

Both queries produce the same SQL string, enabling statement cache hits.

### Named Parameter Rendering

Named parameters use their declared name:

```csharp
var minAge = new Param<int>("minAge");
User.Columns.Age > minAge    // renders: "age > @minAge"
```

### NULL Handling

```csharp
col == null    // renders: "col IS NULL"
col != null    // renders: "col IS NOT NULL"
col.IsNull()   // renders: "col IS NULL"
```

SQL `= NULL` and `!= NULL` are never emitted — SQLite's NULL semantics require `IS NULL` / `IS NOT NULL`.

### Column Name Rendering

Column names are taken from the `ColumnRef<T>` at expression build time, which already reflects any `[Column("name")]` attribute rename. The renderer emits the column name directly — no further lookup is needed at render time.

---

## Statement Caching Interaction

The expression system is designed to produce template-stable SQL. The statement cache (see query builder spec) keys on the SQL string. Callers using literal values in expressions benefit from this automatically. Callers using named `Param<T>` also benefit and additionally decouple the template from the value at the call site.

---

## Limitations

- No support for subquery expressions in this version
- No `EXISTS` / `NOT EXISTS`
- No arithmetic expressions (`col + 1 > 5`) — column arithmetic must be done in raw SQL
- `LIKE` pattern matching is case-sensitive per SQLite's default collation; case-insensitive LIKE requires raw SQL with `COLLATE NOCASE`
