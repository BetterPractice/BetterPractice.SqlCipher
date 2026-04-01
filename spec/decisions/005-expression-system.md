# ADR 005 — Query Expression System

**Status:** Accepted
**Date:** 2026-03-30
**Author:** Holly Schilling

---

## Context

The query builder and Entity layer need a way for callers to express WHERE predicates and other conditions in a type-safe, composable manner. Two approaches were evaluated:

- **LINQ Expression trees** (`Expression<Func<T, bool>>`) — familiar C# syntax, but require tree-walking to generate SQL (cannot use `.Compile()` — AOT-unsafe). Walking arbitrary LINQ trees to SQL is a large, complex problem with inevitable gaps that fail at runtime despite appearing syntactically valid. EF Core's LINQ provider is one of the most complex components in that codebase. First-hand experience on this project confirms the maintenance burden is unacceptable.

- **Custom expression system** — an `IExpression` type with concrete implementations and C# operator overloads. Typed column references are generated per entity by the source generator. Nearly as readable as LINQ predicates; fully AOT-safe; predictable failure modes at compile time rather than runtime.

---

## Decision

Use a **custom expression system** with operator overloading.

### Types

**`IExpression`** — marker interface for all expression nodes. Implemented by all concrete expression types.

**Concrete types** (produced by operator overloads and factory methods, not constructed directly by callers):

| Type | Represents |
|---|---|
| `CompareExpression` | `column OP value` (=, !=, <, <=, >, >=) |
| `ColumnCompareExpression` | `column OP column` (for JOIN ON) |
| `AndExpression` | `left & right` |
| `OrExpression` | `left \| right` |
| `NotExpression` | `!expr` |
| `IsNullExpression` | `IS NULL` / `IS NOT NULL` |
| `BetweenExpression` | `BETWEEN lower AND upper` |
| `InExpression` | `IN (...)` |
| `LikeExpression` | `LIKE pattern` |

**`ColumnRef<T>`** — a typed reference to a column, carrying the column name (respecting `[Column]` renames). Produced by the source generator as members of a static `Columns` class per entity. Supports operator overloads:

```csharp
// Comparison operators — produce CompareExpression
columnRef == value
columnRef != value
columnRef < value
columnRef <= value
columnRef > value
columnRef >= value

// Logical operators — produce And/Or/NotExpression
expr & expr    // AND
expr | expr    // OR
!expr          // NOT
```

Factory methods on `ColumnRef<T>` for constructs that cannot use operators:
```csharp
col.IsNull()
col.IsNotNull()
col.Between(lower, upper)
col.In(value1, value2, ...)
col.Like(pattern)
```

### Source-Generated Column References

For each entity type, the source generator emits a static `Columns` nested class:

```csharp
// For entity: class User : IEntity<int>
// Generator emits:
static class User
{
    public static class Columns
    {
        public static ColumnRef<int> Id { get; } = new("id");
        public static ColumnRef<string> Name { get; } = new("name");
        public static ColumnRef<int> Age { get; } = new("age");
    }
}
```

Call site example:
```csharp
db.Fetch<User>(User.Columns.Age > 18 & User.Columns.Name == "Alice");
```

### Parameter Binding

Literal values on the RHS of expressions are auto-named (e.g., `_0`, `_1`) when rendered to SQL, keeping the SQL string template-stable across different literal values. This enables statement caching — two queries with different literal values but the same structure produce identical SQL strings.

Named parameters are also supported via `Param<T>`:
```csharp
var minAge = new Param<int>("minAge");
var expr = User.Columns.Age > minAge;
db.Fetch<User>(expr, minAge.Set(18));
```

---

## Consequences

**Enables:**
- Full AOT compatibility — no expression tree compilation
- Compile-time column name safety via generated `Columns` classes
- `[Column]` rename attribute respected automatically in generated references
- Composable, readable predicates with C# operators
- Template-stable SQL strings for statement cache hits

**Constrains:**
- `&&` and `||` cannot be overloaded in C#; callers use `&` and `|` for AND/OR. This is a known .NET convention used by other query libraries and is documented clearly.
- Cross-entity column comparisons (JOIN ON) require manually constructed `ColumnCompareExpression` — no operator shorthand between two different entity `Columns` classes
- Only properties with generated `Columns` entries can be used in typed expressions; raw string column references are available as an escape hatch for untyped queries

---

## Supersedes / Superseded by

None.
