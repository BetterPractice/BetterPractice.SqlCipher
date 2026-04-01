# Interface: Expression Types

> Type contracts for the expression system.
> See `/spec/features/expressions.md` for behavioral specification.
> See ADR 005 (`/spec/decisions/005-expression-system.md`) for rationale.

---

## `IExpression`

Marker interface for all expression nodes. Implemented by all concrete expression types. Not intended to be implemented by callers — all expression instances are produced by `ColumnRef<T>` operators and factory methods.

```csharp
public interface IExpression { }
```

---

## `ColumnRef<T>`

A typed reference to a named column. Instances are produced by the source generator as members of each entity's `Columns` class. May also be constructed directly for raw SQL query builder use.

```csharp
public sealed class ColumnRef<T>
{
    /// <summary>The SQL column name, as it appears in the database (post-rename).</summary>
    public string ColumnName { get; }

    public ColumnRef(string columnName);

    // Comparison operators — produce CompareExpression
    public static CompareExpression operator ==(ColumnRef<T> col, T value);
    public static CompareExpression operator !=(ColumnRef<T> col, T value);
    public static CompareExpression operator <(ColumnRef<T> col, T value);
    public static CompareExpression operator <=(ColumnRef<T> col, T value);
    public static CompareExpression operator >(ColumnRef<T> col, T value);
    public static CompareExpression operator >=(ColumnRef<T> col, T value);

    // Null checks via == / != null
    public static IsNullExpression operator ==(ColumnRef<T> col, T? value) where T : class;
    public static IsNullExpression operator !=(ColumnRef<T> col, T? value) where T : class;

    // Comparison against a named parameter
    public static CompareExpression operator ==(ColumnRef<T> col, Param<T> param);
    public static CompareExpression operator !=(ColumnRef<T> col, Param<T> param);
    public static CompareExpression operator <(ColumnRef<T> col, Param<T> param);
    public static CompareExpression operator <=(ColumnRef<T> col, Param<T> param);
    public static CompareExpression operator >(ColumnRef<T> col, Param<T> param);
    public static CompareExpression operator >=(ColumnRef<T> col, Param<T> param);

    // Factory methods
    public IsNullExpression IsNull();
    public IsNullExpression IsNotNull();
    public BetweenExpression Between(T lower, T upper);
    public InExpression In(params T[] values);
    public LikeExpression Like(string pattern);

    // Column-to-column comparison (for JOIN ON)
    public ColumnCompareExpression IsEqualTo(ColumnRef<T> other);
    public ColumnCompareExpression IsNotEqualTo(ColumnRef<T> other);
}
```

---

## Concrete Expression Types

All are `sealed` and implement `IExpression`. Not constructed directly by callers.

```csharp
public sealed class CompareExpression : IExpression
{
    public string ColumnName { get; }
    public ComparisonOperator Operator { get; }
    public object? Value { get; }         // literal value or Param reference
    public bool IsParam { get; }
    public string? ParamName { get; }     // set when IsParam is true
}

public sealed class ColumnCompareExpression : IExpression
{
    public string LeftColumnName { get; }
    public ComparisonOperator Operator { get; }
    public string RightColumnName { get; }
}

public sealed class AndExpression : IExpression
{
    public IExpression Left { get; }
    public IExpression Right { get; }

    public static AndExpression operator &(IExpression left, IExpression right);
}

public sealed class OrExpression : IExpression
{
    public IExpression Left { get; }
    public IExpression Right { get; }

    public static OrExpression operator |(IExpression left, IExpression right);
}

public sealed class NotExpression : IExpression
{
    public IExpression Operand { get; }

    public static NotExpression operator !(IExpression operand);
}

public sealed class IsNullExpression : IExpression
{
    public string ColumnName { get; }
    public bool IsNull { get; }    // true = IS NULL, false = IS NOT NULL
}

public sealed class BetweenExpression : IExpression
{
    public string ColumnName { get; }
    public object Lower { get; }
    public object Upper { get; }
}

public sealed class InExpression : IExpression
{
    public string ColumnName { get; }
    public IReadOnlyList<object> Values { get; }
}

public sealed class LikeExpression : IExpression
{
    public string ColumnName { get; }
    public string Pattern { get; }
}
```

---

## `ComparisonOperator`

```csharp
public enum ComparisonOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual
}
```

---

## `Param<T>` and `ParamBinding`

```csharp
/// <summary>A named, typed parameter placeholder for use in expressions.</summary>
public sealed class Param<T>
{
    public string Name { get; }
    public Param(string name);

    /// <summary>Binds a value to this parameter for a specific query execution.</summary>
    public ParamBinding Bind(T value);
}

/// <summary>A type-erased bound parameter value, passed alongside an expression at query time.</summary>
public sealed class ParamBinding
{
    public string Name { get; }
    public object? Value { get; }
}
```

---

## Source-Generated `Columns` Classes

For each entity type implementing `IEntity<TId>`, the source generator emits a nested static `Columns` class containing one `ColumnRef<T>` per mapped property.

Example for:
```csharp
public class User : IEntity<int>
{
    [PrimaryKey]
    public int Id { get; init; }
    public string Name { get; init; }
    [Column("birth_year")]
    public int BirthYear { get; init; }
}
```

Generator emits (into a companion type, not the entity itself — see ADR 004):
```csharp
public static class UserColumns
{
    public static ColumnRef<int> Id { get; } = new("id");
    public static ColumnRef<string> Name { get; } = new("name");
    public static ColumnRef<int> BirthYear { get; } = new("birth_year");
}
```

The generated class name is `{EntityName}Columns`. The generator respects `[Column("name")]` attribute renames when setting `ColumnName`.
