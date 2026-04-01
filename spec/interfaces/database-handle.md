# Interface: DatabaseHandle

> The primary public API surface. All database interaction goes through this type.
> See `/spec/GLOSSARY.md` for term definitions.
> See `/spec/domain/domain-model.md` for lifecycle and invariants.

---

## Overview

`DatabaseHandle` is the root public type. It is obtained via static factory methods (`Create` and `Open`) and must be disposed when no longer needed. It is safe to use from multiple threads without external synchronization.

---

## Static Factory Methods

### `DatabaseHandle.Create`

Creates a new encrypted database file.

```
static Task<DatabaseHandle> Create(
    string path,
    string passphrase,
    CipherConfiguration? cipherConfiguration = null)
```

**Parameters:**
- `path` — Absolute path where the database file will be created. The directory must exist. The file must not already exist.
- `passphrase` — The Passphrase used to encrypt the database. Must not be null or empty.
- `cipherConfiguration` — Optional. If null, `CipherConfiguration.Default` is used.

**Returns:** An open `DatabaseHandle` ready to accept operations.

**Errors:**
- File already exists at `path` → explicit exception (not silent overwrite)
- Directory does not exist → explicit exception
- `passphrase` is null or empty → explicit exception before any native call
- Native SQLCipher error → wrapped exception with SQLCipher error code and message

---

### `DatabaseHandle.Open`

Opens an existing encrypted database file.

```
static Task<DatabaseHandle> Open(
    string path,
    string passphrase)
```

**Parameters:**
- `path` — Absolute path to an existing database file.
- `passphrase` — The Passphrase used to decrypt the database. Must not be null or empty.

**Returns:** An open `DatabaseHandle` ready to accept operations.

**Errors:**
- File does not exist at `path` → explicit exception
- Passphrase incorrect → explicit exception (SQLCipher returns an error on first read if key is wrong)
- `passphrase` is null or empty → explicit exception before any native call
- Native SQLCipher error → wrapped exception with SQLCipher error code and message

---

## Instance Methods

### `Execute`

Executes a SQL statement that returns no rows (INSERT, UPDATE, DELETE, DDL).

```
Task<int> Execute(string sql, IReadOnlyDictionary<string, object?>? parameters = null)
```

**Returns:** Number of rows affected.

**Errors:**
- SQL error → explicit exception with SQLCipher error code and message
- Handle disposed → explicit exception

---

### `Query`

Executes a SQL statement that returns rows.

```
Task<ResultSet> Query(string sql, IReadOnlyDictionary<string, object?>? parameters = null)
```

**Returns:** A fully materialized `ResultSet`. May be empty.

**Errors:**
- SQL error → explicit exception with SQLCipher error code and message
- Handle disposed → explicit exception

---

### `Rekey`

Changes the Passphrase on the database. Rewrites the entire database file. This operation is expensive and blocking to other operations while in progress (serialized through the Operation Queue like all other operations).

```
Task Rekey(string newPassphrase)
```

**Parameters:**
- `newPassphrase` — The new Passphrase. Must not be null or empty.

**Errors:**
- `newPassphrase` is null or empty → explicit exception
- Native SQLCipher error → wrapped exception
- Handle disposed → explicit exception

---

### `Execute` (BuiltQuery overload)

Executes a pre-built query that returns no rows.

```
Task<int> Execute(BuiltQuery query)
```

---

### `Execute` (DDL overloads)

```
Task Execute(CreateTable statement)
Task Execute(AlterTable statement)
Task Execute(DropTable statement)
Task Execute(CreateIndex statement)
Task Execute(DropIndex statement)
```

DDL statements bypass the statement cache.

---

### `Query` (BuiltQuery overload)

```
Task<ResultSet> Query(BuiltQuery query)
```

---

### `ExecuteTransaction`

Executes a group of operations atomically within a single transaction. All operations in `body` run inline on the background task — no other operations can interleave. The return value of `body` is passed through to the caller.

```
Task<T> ExecuteTransaction<T>(Func<ITransactionContext, T> body)
Task     ExecuteTransaction(Action<ITransactionContext> body)
```

The body is synchronous. Async bodies are intentionally not supported — awaiting inside a transaction body would release the calling thread while the database connection is held open inside the Operation Queue worker, which would allow other operations to queue up behind a stalled transaction without being able to make progress.

**Behavior:**
- `BEGIN` before invoking `body`
- `COMMIT` if `body` completes without exception
- `ROLLBACK` if `body` throws; the exception is re-thrown to the caller
- The `Action` overload is equivalent to `ExecuteTransaction<bool>` with a discarded return value

**Errors:**
- Body exception → `ROLLBACK` attempted, original exception re-thrown
- Both body exception and `ROLLBACK` failure → `AggregateException`
- Handle disposed → explicit exception

See `/spec/features/transactions.md` for full behavioral specification.

---

### `Migrate`

Applies all pending migrations in the order provided. Safe to call on every launch.

```
Task Migrate(IReadOnlyList<IMigration> migrations)
```

**Errors:**
- Duplicate migration IDs in the list → `MigrationException` before any migration runs
- Migration `Up` failure → `MigrationException` wrapping the underlying exception; no further migrations run

See `/spec/features/migrations.md` for full behavioral specification.

---

### `Rollback`

Rolls back all migrations applied after `targetId`. `targetId` itself remains applied.

```
Task Rollback(string targetId, IReadOnlyList<IMigration> migrations)
```

**Errors:**
- `targetId` not in applied set → `MigrationException`
- Migration definition not found in supplied list during rollback → `MigrationException`
- Migration `Down` failure → `MigrationException`; no further rollbacks run

---

### Entity Operations

Entity operations are not called directly on `DatabaseHandle` — the source generator emits typed extension methods per entity type that consumers call instead. See `/spec/interfaces/entity.md` for the generated extension method signatures and `/spec/features/entity-orm.md` for behavioral specification.

The internal generic methods (not called directly):

```csharp
Task<T> Save<T, TId>(T entity) where T : IEntity<TId> where TId : notnull
Task<IReadOnlyList<T>> Save<T, TId>(IReadOnlyList<T> entities) where T : IEntity<TId> where TId : notnull
Task<IReadOnlyList<T>> Fetch<T, TId>() where T : IEntity<TId> where TId : notnull
Task<IReadOnlyList<T>> Fetch<T, TId>(IExpression where, params ParamBinding[] bindings) where T : IEntity<TId> where TId : notnull
Task<T?> FetchOne<T, TId>(TId id) where T : IEntity<TId> where TId : notnull
Task<int> Delete<T, TId>(TId id) where T : IEntity<TId> where TId : notnull
Task<int> Delete<T, TId>(IExpression where, params ParamBinding[] bindings) where T : IEntity<TId> where TId : notnull
```

---

### `Dispose`

Drains in-flight operations, closes the SQLite connection, and releases all resources. Subsequent calls are no-ops. Operations submitted after disposal are rejected with an explicit exception.

```
void Dispose()
```

`DatabaseHandle` implements `IDisposable`. An async disposal path (`IAsyncDisposable`) is also provided to allow callers to await queue drain.

```
ValueTask DisposeAsync()
```

---

## Properties

| Property | Type | Description |
|---|---|---|
| `Path` | `string` | Absolute path to the database file. Read-only. |
| `CipherConfiguration` | `CipherConfiguration` | The cipher configuration used when the database was created. Read-only. |
| `IsDisposed` | `bool` | True if the handle has been disposed. |

---

## Thread Safety

`DatabaseHandle` is safe to use from multiple threads. All operations are serialized internally through the Operation Queue. Callers do not need to perform any external synchronization.
