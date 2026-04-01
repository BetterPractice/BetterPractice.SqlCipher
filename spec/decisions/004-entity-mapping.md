# ADR 004 — Entity Mapping Strategy

**Status:** Accepted
**Date:** 2026-03-30
**Author:** Holly Schilling

---

## Context

The ORM (Entity) layer needs to map between C# objects and database rows. This requires:

1. Discovering which properties map to which columns
2. Reading property values when writing to the database
3. Writing property values (including auto-assigned primary keys) when reading from the database

Three approaches were evaluated:

- **Reflection** — `PropertyInfo.GetValue` / `SetValue` at runtime. Simple to implement. Not AOT-safe; breaks iOS target.
- **LINQ Expressions** — `Expression<Func<T, TId>>` to locate the PK property, `PropertyInfo.SetValue` for writeback. Ergonomic at the call site but still requires reflection under the hood; not AOT-safe.
- **Source generators** — a Roslyn `IIncrementalGenerator` that analyzes entity types at compile time and emits mapping code. AOT-safe, trim-safe, produces compile-time errors for misconfigured entities.

---

## Decision

All entity mapping — column discovery, row-to-object construction, object-to-row serialization, and primary key writeback — must be implemented via **C# source generators** using the `IIncrementalGenerator` API (incremental generators only; the older `ISourceGenerator` API is not permitted).

Entities are identified by implementing the `IEntity<TId>` interface (defined in `BetterPractice.SqlCipher.core`). The source generator inspects all types implementing this interface and emits:

- A companion mapper class per entity type (see ADR 004 companion type decision below)
- Typed extension methods on `DatabaseHandle` and `ITransactionContext` per entity type, bridging to the internal generic implementation
- Compile-time validation of required attributes (`[PrimaryKey]`, etc.)
- No runtime reflection anywhere in the hot path

Column naming is controlled by a `[Column("col_name")]` attribute on the property. Properties without the attribute use the property name as-is (no automatic case conversion).

Primary key writeback uses a generated setter call — the source generator emits a direct property assignment, not `PropertyInfo.SetValue`. This requires the primary key property to have a `set` accessor (public or `init`-only).

### Call Site Design

`DatabaseHandle` exposes internal generic methods that require two type parameters (`T` and `TId`). Because C# cannot infer `TId` from a constraint on `T`, these generic methods are not called directly by consumers. Instead, the source generator emits typed extension methods per entity that forward to the generic implementation with both type parameters fully specified:

```csharp
// Internal generic method (not called directly):
Task<T> Save<T, TId>(T entity) where T : IEntity<TId> where TId : notnull;

// Generator emits (for User : IEntity<int>):
public static Task<User> Save(this DatabaseHandle db, User entity)
    => db.Save<User, int>(entity);

public static Task<IReadOnlyList<User>> Fetch(this DatabaseHandle db)
    => db.Fetch<User, int>();

public static Task<IReadOnlyList<User>> Fetch(this DatabaseHandle db, IExpression where, params ParamBinding[] bindings)
    => db.Fetch<User, int>(where, bindings);

public static Task<User?> FetchOne(this DatabaseHandle db, int id)
    => db.FetchOne<User, int>(id);

public static Task<int> Delete(this DatabaseHandle db, int id)
    => db.Delete<User, int>(id);

public static Task<int> Delete(this DatabaseHandle db, IExpression where, params ParamBinding[] bindings)
    => db.Delete<User, int>(where, bindings);
```

Consumer call sites use the extension methods with full type inference from the argument:

```csharp
await db.Save(user);                            // T inferred as User
await db.Fetch<User>();                         // or db.Fetch(user) — no arg, must specify
await db.Fetch(UserColumns.Age > 18);           // expression overload, T inferred from extension
await db.FetchOne(42);                          // TId inferred as int from the extension
await db.Delete(42);                            // TId inferred as int
```

Unused generated extension methods are pruned by the IL trimmer at publish time with no runtime cost.

---

## Consequences

**Enables:**
- Full AOT compatibility, including iOS
- Trim-safe — no types are accessed via reflection strings
- Compile-time errors for entity misconfiguration (missing PK, unsupported property types)
- Generated code is readable and debuggable

**Constrains:**
- The generator emits a separate companion mapper type per entity (e.g., `UserEntityMapper` for `User`). Entity types require no `partial` modifier and no awareness of the generated code — they are plain C# types implementing `IEntity<TId>`.
- This decision may be revisited if the companion type pattern proves difficult during implementation, at which point `partial` classes would be the fallback.
- The `IIncrementalGenerator` API must be used — this is required for incremental caching behaviour and IDE responsiveness. The generator only re-runs when entity type definitions change.
- The source generator is a separate project within the solution (`BetterPractice.SqlCipher.Generator`)
- Developers need .NET 8 SDK with Roslyn source generator support (already required by the .NET 8 minimum target)
- Properties that should map to columns must have a `set` or `init` accessor; purely computed properties are ignored by the generator

---

## Supersedes / Superseded by

None.
