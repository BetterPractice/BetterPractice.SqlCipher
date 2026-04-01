# BetterPractice.SqlCipher

An encrypted embedded database for .NET, built on [SQLCipher](https://www.zetetic.net/sqlcipher/) (SQLite with 256-bit AES encryption). Designed for concurrent server workloads — all database operations are serialized through a `Channel<T>`, so no external locking is required.

- Thread-safe by design — submit operations from any thread
- Encrypted at rest using SQLCipher 4 defaults (AES-256, PBKDF2-HMAC-SHA512, 256k iterations)
- Fluent query builder — no raw string concatenation required
- Typed expression system for WHERE clauses
- Source-generated ORM — zero reflection, AOT/iOS compatible
- Explicit migration system with rollback support

---

## Installation

```
dotnet add package BetterPractice.SqlCipher.Core
dotnet add package BetterPractice.SqlCipher.runtime.osx-arm64   # or your target RID
```

The runtime package bundles the compiled `libsqlcipher` native binary for the target platform. Supported RIDs: `osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`.

---

## Opening a Database

```csharp
// Create a new encrypted database
await using var db = await DatabaseHandle.Create("/path/to/app.db", "your-passphrase");

// Open an existing database
await using var db = await DatabaseHandle.Open("/path/to/app.db", "your-passphrase");
```

`DatabaseHandle` is `IAsyncDisposable`. Always use `await using` to ensure the operation queue drains cleanly before the connection is closed.

### Custom cipher configuration

```csharp
var config = new CipherConfiguration(
    pageSize:      4096,
    kdfIterations: 256_000,
    kdfAlgorithm:  KdfAlgorithm.Pbkdf2HmacSha512,
    hmacAlgorithm: HmacAlgorithm.Sha512);

await using var db = await DatabaseHandle.Create("/path/to/app.db", "your-passphrase", config);
```

Cipher configuration is fixed at database creation and cannot be changed afterward.

### Rekeying

```csharp
await db.Rekey("new-passphrase");
```

Rekeying rewrites the entire database and is serialized through the operation queue like any other operation.

---

## Raw SQL

### Execute

```csharp
// No parameters
await db.Execute("CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL)");

// Named parameters
int rowsAffected = await db.Execute(
    "INSERT INTO users (name) VALUES (@name)",
    new Dictionary<string, object?> { ["name"] = "Alice" });
```

### Query

```csharp
ResultSet rows = await db.Query(
    "SELECT id, name FROM users WHERE name = @name",
    new Dictionary<string, object?> { ["name"] = "Alice" });

foreach (Row row in rows)
{
    int    id   = row["id"].AsInt32();
    string name = row["name"].AsText();
}
```

Column values are accessed by name or index and expose typed accessors: `AsInt32()`, `AsInt64()`, `AsDouble()`, `AsText()`, `AsBlob()`, and `IsNull`.

### Transactions

```csharp
// Void body — use Action overload
await db.ExecuteTransaction(ctx =>
{
    ctx.Execute("UPDATE accounts SET balance = balance - 100 WHERE id = 1");
    ctx.Execute("UPDATE accounts SET balance = balance + 100 WHERE id = 2");
});

// Returning a value — use generic Func overload
int rowsInserted = await db.ExecuteTransaction(ctx =>
    ctx.Execute("INSERT INTO accounts VALUES (3, 250)"));
```

If the body throws, the transaction is rolled back automatically. Reads within the body see uncommitted changes from earlier statements in the same transaction.

The body must be synchronous — async bodies are not supported. Awaiting inside a transaction would release the worker thread while the database connection is held, stalling all other operations.

---

## Query Builder

The fluent query builder constructs SQL without string concatenation. All builders are immutable — each method returns a new instance.

### Select

```csharp
BuiltQuery q = Select.From("users")
    .Columns("id", "name", "email")
    .Where(new ColumnRef<string>("status") == "active")
    .OrderBy("name", SortDirection.Ascending)
    .Limit(20)
    .Offset(40)
    .Build();

ResultSet rows = await db.Query(q);
```

### Insert

```csharp
BuiltQuery q = Insert.Into("users")
    .Set("name",  "Bob")
    .Set("email", "bob@example.com")
    .Build();

await db.Execute(q);
```

### Update

```csharp
var id = new Param<int>("id");

BuiltQuery q = Update.Table("users")
    .Set("name", "Robert")
    .Where(new ColumnRef<int>("id") == id, id.Bind(42))
    .Build();

await db.Execute(q);
```

### Create Table

```csharp
BuiltQuery q = CreateTable.Named("events")
    .Column("id",         ColumnType.Integer, ColumnConstraint.PrimaryKey, ColumnConstraint.AutoIncrement)
    .Column("user_id",    ColumnType.Integer, ColumnConstraint.NotNull)
    .Column("name",       ColumnType.Text,    ColumnConstraint.NotNull)
    .Column("created_at", ColumnType.Text,    ColumnConstraint.NotNull)
    .Column("payload",    ColumnType.Blob)
    .IfNotExists()
    .Build();

await db.Execute(q);
```

---

## Expression System

Expressions build typed WHERE clause fragments using operator overloads. They compose with `&` (AND), `|` (OR), and `!` (NOT).

```csharp
// Simple comparison
IExpression active = new ColumnRef<string>("status") == "active";

// Range check
IExpression senior = new ColumnRef<int>("age") >= 65;

// Compound expression
IExpression expr = active & senior;

ResultSet rows = await db.Query(
    Select.From("users").Where(expr).Build());
```

### Named parameters in expressions

Use `Param<T>` when you want to bind a value by name rather than have the builder auto-generate a parameter name:

```csharp
var minScore = new Param<double>("minScore");
var maxScore = new Param<double>("maxScore");

IExpression range = new ColumnRef<double>("score") >= minScore
                  & new ColumnRef<double>("score") <= maxScore;

BuiltQuery q = Select.From("leaderboard")
    .Where(range, minScore.Bind(50.0), maxScore.Bind(100.0))
    .Build();
```

### Null checks

```csharp
IExpression hasEmail    = new ColumnRef<string>("email").IsNotNull();
IExpression missingName = new ColumnRef<string>("name").IsNull();
```

### IN clause

```csharp
IExpression filter = new ColumnRef<int>("category_id").In(1, 2, 3);
```

### BETWEEN

```csharp
IExpression range = new ColumnRef<int>("age").Between(18, 65);
```

### LIKE

```csharp
IExpression search = new ColumnRef<string>("email").Like("%@example.com");
```

---

## Migrations

Migrations implement `IMigration` and are applied in order. Already-applied migrations are skipped automatically. Each migration runs in its own transaction — a failure records no version and does not affect previously applied migrations.

```csharp
public sealed class CreateUsersTable : IMigration
{
    public string Id => "001-create-users";

    public void Up(MigrationContext ctx)
    {
        ctx.Execute(
            CreateTable.Named("users")
                .Column("id",    ColumnType.Integer, ColumnConstraint.PrimaryKey, ColumnConstraint.AutoIncrement)
                .Column("name",  ColumnType.Text,    ColumnConstraint.NotNull)
                .Column("email", ColumnType.Text)
                .IfNotExists()
                .Build());
    }

    public void Down(MigrationContext ctx)
    {
        ctx.Execute("DROP TABLE users");
    }
}

public sealed class AddUserScore : IMigration
{
    public string Id => "002-add-user-score";

    public void Up(MigrationContext ctx)
    {
        ctx.Execute("ALTER TABLE users ADD COLUMN score REAL NOT NULL DEFAULT 0");
    }

    public void Down(MigrationContext ctx)
    {
        // SQLite does not support DROP COLUMN prior to 3.35.0
        ctx.Execute("CREATE TABLE users_new (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, email TEXT)");
        ctx.Execute("INSERT INTO users_new SELECT id, name, email FROM users");
        ctx.Execute("DROP TABLE users");
        ctx.Execute("ALTER TABLE users_new RENAME TO users");
    }
}
```

Apply migrations on startup:

```csharp
await db.Migrate(new IMigration[]
{
    new CreateUsersTable(),
    new AddUserScore(),
});
```

Roll back to a specific migration (exclusive — migrations after `targetId` are reversed in order):

```csharp
await db.Rollback("001-create-users", new IMigration[]
{
    new CreateUsersTable(),
    new AddUserScore(),
});
```

Applied migration IDs are tracked in the `_bp_migrations` system table.

---

## ORM

The source generator reads entity types annotated with `[Table]`, `[PrimaryKey]`, and `[Column]` and emits a companion mapper at compile time. No reflection is used — the generated code is fully AOT-compatible.

### Define an entity

```csharp
[Table("users")]
public sealed class User : IEntity<int>
{
    [PrimaryKey]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("email")]
    public string? Email { get; set; }

    [Column("score")]
    public double Score { get; set; }

    [NotMapped]
    public string DisplayName => Name.ToUpperInvariant(); // not persisted
}
```

### Save (insert or update)

```csharp
// Insert — Id is 0, generator uses INSERT and populates Id from last_insert_rowid
var user = new User { Name = "Alice", Email = "alice@example.com", Score = 9.5 };
user = await db.Save<User, int>(user);
// user.Id is now the auto-assigned primary key

// Update — Id is non-zero, generator uses INSERT OR REPLACE
user.Score = 10.0;
await db.Save<User, int>(user);
```

### Save multiple

```csharp
var users = new[]
{
    new User { Name = "Bob" },
    new User { Name = "Carol" },
};
await db.SaveAll<User, int>(users);
```

### Fetch all

```csharp
IReadOnlyList<User> all = await db.Fetch<User, int>();
```

### Fetch with expression

```csharp
IReadOnlyList<User> topScorers = await db.Fetch<User, int>(
    new ColumnRef<double>("score") >= 8.0);
```

### Fetch one by primary key

```csharp
User? user = await db.FetchOne<User, int>(42);
```

### Delete by primary key

```csharp
await db.Delete<User, int>(42);
```

### Delete with expression

```csharp
await db.Delete<User, int>(new ColumnRef<double>("score") < 1.0);
```

---

## Thread Safety

All operations on `DatabaseHandle` are thread-safe. Callers may submit operations concurrently from any number of threads; they are queued and executed serially in submission order. No external locking is needed.

---

## License

BetterPractice.SqlCipher is released under the [MIT License](LICENSE).

This package includes compiled binaries derived from [SQLCipher](https://www.zetetic.net/sqlcipher/) (BSD 3-Clause, Copyright © 2008-2023 ZETETIC LLC) and [SQLite](https://www.sqlite.org/) (public domain). See [THIRD_PARTY_NOTICES](THIRD_PARTY_NOTICES) for full license text.
