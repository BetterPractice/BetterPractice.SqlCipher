# Glossary

> Canonical terminology for BetterPractice.SqlCipher.
> All code, comments, documentation, and specs must use these terms exactly.
> If a term appears in code that is not here, add it before the PR merges.

---

## Terms

### Amalgamation
The single-file distribution of the SQLite source code (`sqlite3.c` and `sqlite3.h`). BetterPractice.SqlCipher compiles SQLCipher from a snapshot of this file with SQLCipher patches applied. The amalgamation snapshot is checked into the repository and updated deliberately — not automatically.

### Cipher Configuration
The set of SQLCipher-specific parameters that govern encryption behavior: **Page Size**, **KDF Algorithm**, **KDF Iterations**, and **HMAC Algorithm**. Cipher Configuration is fixed at **Database Creation** and cannot be changed without migration. Exposed as `CipherConfiguration`.

### Database
A single encrypted SQLite database file managed by BetterPractice.SqlCipher. A Database has exactly one **Database Handle** through which all operations are performed.

### Database Creation
The act of initializing a new, empty encrypted database file with a **Passphrase** and **Cipher Configuration**. Distinct from **Opening** an existing database. Page size and cipher parameters are written during creation and are immutable afterward.

### Database Handle
The primary object through which callers interact with a **Database**. Owns the underlying SQLite connection and the **Operation Queue**. All operations are submitted through the Database Handle. Exposed as `DatabaseHandle`.

### HMAC Algorithm
The algorithm used by SQLCipher to compute per-page authentication codes. Part of **Cipher Configuration**. Default: SHA512. Valid values: SHA1, SHA256, SHA512.

### KDF Algorithm
The key derivation function used to derive the encryption key from the **Passphrase**. Part of **Cipher Configuration**. Default: PBKDF2-HMAC-SHA512.

### KDF Iterations
The number of iterations applied by the **KDF Algorithm**. Higher values increase brute-force resistance at the cost of open/rekey time. Part of **Cipher Configuration**. Default: 256,000 (SQLCipher 4 default).

### Native Library
The platform-specific compiled binary (`libsqlcipher.dylib`, `libsqlcipher.so`, `sqlcipher.dll`) that contains the SQLCipher implementation. Bundled in the appropriate `BetterPractice.SqlCipher.runtime.<rid>` package. Loaded via `LibraryImport`.

### Opening
The act of connecting to an existing encrypted **Database** file with a **Passphrase**. Distinct from **Database Creation**. If the passphrase is incorrect, opening fails with an explicit error — it does not silently produce corrupt reads.

### Operation
A unit of work submitted to the **Operation Queue** for execution against the **Database**. Operations are executed serially in submission order. An Operation carries its SQL, parameters, and a `TaskCompletionSource` for returning the result to the caller.

### Operation Queue
The `Channel<Operation>` that serializes all work submitted to a **Database Handle**. There is exactly one Operation Queue per Database Handle. Callers submit Operations and await their results; the queue processes them one at a time on a dedicated background task.

### Page Size
The size in bytes of each SQLite storage page within the database file. Part of **Cipher Configuration**, but also a SQLite storage parameter. Must be a power of two between 512 and 65536. Default: 4096. Must be set at **Database Creation**; cannot be changed afterward without a full migration.

### Passphrase
A caller-supplied string used as input to the **KDF Algorithm** to derive the encryption key. Never stored. Never logged. The Passphrase is supplied at **Database Creation** and **Opening**, and as both old and new values during **Rekeying**.

### P/Invoke Layer
The internal static partial class(es) that declare `LibraryImport`-attributed method signatures mapping to SQLCipher native functions. Not part of the public API. Consumers never interact with the P/Invoke Layer directly.

### Rekeying
The operation of changing the **Passphrase** on an existing **Database** without migrating data to a new file. Rekeying is expensive (rewrites the entire database) and is executed as a single serialized **Operation** through the **Operation Queue**. Exposed as a method on `DatabaseHandle`.

### Result Set
The rows returned by a query **Operation**. Returned as an ordered sequence of **Rows**.

### Row
A single record in a **Result Set**, represented as an ordered, named collection of **Values**.

### Runtime Package
A NuGet package containing the **Native Library** for a specific platform Runtime Identifier (RID). Named `BetterPractice.SqlCipher.runtime.<rid>`. Consumers reference the core package plus the runtime package(s) matching their deployment targets.

### Value
A single field within a **Row**. SQLCipher's underlying type system is SQLite's: NULL, INTEGER, REAL, TEXT, BLOB. Values are surfaced to callers in a typed manner where the column type is known, or as a discriminated type where it is not.

### Built Query
The output of a query builder's `Build()` method. Contains a template-stable SQL string and a dictionary of named parameter bindings. Passed to `DatabaseHandle.Execute` or `DatabaseHandle.Query` for execution.

### Cipher Configuration
See `CipherConfiguration` — the set of SQLCipher encryption parameters fixed at Database Creation.

### Column Reference
A `ColumnRef<T>` instance representing a typed reference to a named column. Used in the expression system to build type-safe predicates. Produced by the source generator for entity types, or constructed directly for raw query builder use.

### Companion Mapper
A source-generated internal class that handles all mapping between an entity type and database rows. One companion mapper is generated per entity type. Not part of the public API. Named `{EntityName}EntityMapper`.

### Complex Column Strategy
An `IComplexColumnStrategy` implementation that serializes a property value to a single SQLite column value and back. The default strategy is JSON. Applied when a mapped property's type cannot be stored directly as a SQLite scalar.

### Entity
A plain C# class or struct implementing `IEntity<TId>`. Represents a row in a database table. Mapped by the source generator via a Companion Mapper.

### Expression
An `IExpression` node representing a SQL predicate fragment. Composed from `ColumnRef<T>` operator overloads and factory methods. Rendered to a parameterized SQL fragment by an internal `ExpressionRenderer`.

### Migration
An `IMigration` implementation representing a single discrete schema change with an `Up` (apply) and `Down` (reverse) path. Identified by a stable string `Id`.

### Migration Context
A `MigrationContext` instance provided to `IMigration.Up` and `IMigration.Down`. Exposes database operations scoped to the migration's transaction. Not part of the public API surface beyond the migration interface.

### Param / ParamBinding
A `Param<T>` is a named, typed parameter placeholder used in expressions and query builders. `Bind(value)` produces a `ParamBinding` (type-erased) passed at query execution time alongside the expression.

### Statement Cache
An internal LRU cache keyed on SQL strings. Stores compiled SQLite statement handles for reuse across executions. DDL and PRAGMA statements are excluded. Default capacity: 64 entries.

### Transaction Context
An `ITransactionContext` instance provided to the `body` callback of `DatabaseHandle.ExecuteTransaction`. Exposes inline (non-queued) database operations within the active transaction scope. Must not be stored or used outside the callback.

---

### Reserved Table Names

Table names prefixed with `_bp_` are reserved for internal use by BetterPractice.SqlCipher. Consumers must not create tables with these names. Currently reserved:

| Table name | Purpose |
|---|---|
| `_bp_meta` | Sentinel table written at Database Creation to force encryption header initialization |
| `_bp_migrations` | Reserved for the future migrations feature |

---

### WAL Mode
Write-Ahead Logging — the SQLite journal mode used by all databases managed by BetterPractice.SqlCipher. WAL mode is always enabled; it is not configurable. WAL mode provides better write performance and reduced contention compared to the default rollback journal.
