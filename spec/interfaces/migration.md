# Interface: Migration Types

> Defines IMigration, MigrationContext, and MigrationException.
> See `/spec/features/migrations.md` for behavioral specification.

---

## `IMigration`

```csharp
public interface IMigration
{
    /// <summary>
    /// Stable, unique identifier for this migration.
    /// Recommended format: "NNN-short-description" (e.g. "001-create-users").
    /// Must not change after the migration has been applied to any database.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Applies this migration. Called within a transaction — throw to roll back.
    /// </summary>
    void Up(MigrationContext context);

    /// <summary>
    /// Reverses this migration. Called within a transaction — throw to roll back.
    /// A no-op body is permitted but must be a conscious choice.
    /// </summary>
    void Down(MigrationContext context);
}
```

---

## `MigrationContext`

Provided to `Up` and `Down`. Exposes database operations scoped to the migration's transaction. All methods execute synchronously — they run inline on the background task within the active transaction.

```csharp
public sealed class MigrationContext
{
    // Raw SQL
    int Execute(string sql, IReadOnlyDictionary<string, object?>? parameters = null);
    ResultSet Query(string sql, IReadOnlyDictionary<string, object?>? parameters = null);

    // DDL builders
    void Execute(CreateTable statement);
    void Execute(AlterTable statement);
    void Execute(DropTable statement);
    void Execute(CreateIndex statement);
    void Execute(DropIndex statement);

    // DML builders
    int Execute(Insert statement);
    int Execute(Update statement);
}
```

`MigrationContext` is provided by the library and must not be constructed by callers. It is scoped to a single migration invocation and must not be stored or used outside the `Up`/`Down` method body — documented by convention (no compile-time enforcement).

---

## `MigrationException`

```csharp
public sealed class MigrationException : Exception
{
    /// <summary>The Id of the migration that failed.</summary>
    public string MigrationId { get; }

    /// <summary>The phase during which the failure occurred.</summary>
    public MigrationPhase Phase { get; }
}

public enum MigrationPhase
{
    Up,
    Down,
    RollbackLookup   // migration definition not found in supplied list during rollback
}
```

`MigrationException.InnerException` carries the underlying cause (`SqlCipherException` for native errors, or the exception thrown by the migration body).

---

## `DatabaseHandle` Migration Methods

```csharp
// Apply all pending migrations in the order provided.
Task Migrate(IReadOnlyList<IMigration> migrations);

// Roll back all migrations applied after targetId (targetId remains applied).
Task Rollback(string targetId, IReadOnlyList<IMigration> migrations);
```

Both methods are submitted as single Operations through the Operation Queue — no other database activity can interleave during migration or rollback.
