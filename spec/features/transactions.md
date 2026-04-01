# Feature: Transactions

> Covers explicit transaction scopes for multi-statement atomicity.
> See `/spec/interfaces/database-handle.md` for the API contract.

---

## Overview

A transaction groups multiple operations into a single atomic unit — either all succeed and are committed, or any failure causes a full rollback. Transactions are exposed as a scoped callback on `DatabaseHandle`. All operations within the callback run inline on the background task within the active transaction, preventing any other operation from interleaving.

---

## Transaction Execution

**Trigger:** Caller invokes `DatabaseHandle.ExecuteTransaction<T>(Func<ITransactionContext, T> body)` or the `Action` overload.

The body is **synchronous**. Async bodies are not supported. Awaiting inside a transaction body would release the Operation Queue worker thread while the database connection is held, allowing other enqueued operations to stall indefinitely waiting behind a transaction that cannot proceed. Keeping the body synchronous ensures the entire transaction completes atomically without yielding.

**Steps:**
1. Submit a single composite Operation to the Operation Queue
2. The background consumer:
   a. Executes `BEGIN`
   b. Constructs an `ITransactionContext` backed by the open connection
   c. Invokes `body(context)` synchronously
   d. If the body completes without exception: executes `COMMIT`, returns the body's return value to the caller's `Task<T>`
   e. If the body throws: executes `ROLLBACK`, then re-throws the exception to the caller's awaited `Task<T>`

The `Action` overload is a convenience wrapper over the generic form that discards the return value.

**Serialization guarantee:** Because the entire transaction — `BEGIN`, all body operations, and `COMMIT`/`ROLLBACK` — runs as a single Operation on the queue, no other operation can execute between any two statements in the transaction body.

---

## `ITransactionContext`

Exposes the same data operations as `DatabaseHandle` but executes them synchronously and inline (not re-enqueued). The context is valid only for the duration of the `body` callback.

```csharp
public interface ITransactionContext
{
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

Entity operations (`Save`, `Fetch`, `Delete`) are also available on `ITransactionContext` — see `/spec/interfaces/entity.md`.

**`ITransactionContext` must not be stored or used outside the `body` callback.** There is no compile-time enforcement of this constraint. It is documented here and in XML doc comments on the interface. Storing the context and calling methods after the transaction completes results in undefined behavior.

---

## Savepoints

Savepoints (nested transactions via `SAVEPOINT` / `RELEASE` / `ROLLBACK TO`) are out of scope for this version. Nesting `ExecuteTransaction` calls is not permitted — attempting to do so results in a `SqlCipherException` (SQLite will reject a nested `BEGIN`).

---

## Relationship to Migration Transactions

Migrations use the same transaction mechanism internally but through `MigrationContext`, not `ITransactionContext`. Callers cannot start an `ExecuteTransaction` while a `Migrate` or `Rollback` is in progress — this is naturally prevented by the Operation Queue serialization.

---

## Error Handling

- If `ROLLBACK` fails after a body exception, both the original exception and the rollback failure are surfaced via `AggregateException`
- If the handle is disposed while a transaction body is executing, the body receives a `ObjectDisposedException` on its next context call; rollback is attempted before the connection is closed
