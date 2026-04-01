# Feature: Query Execution

> Covers Execute and Query operations submitted through a DatabaseHandle.
> See `/spec/interfaces/database-handle.md` and `/spec/interfaces/result-types.md`.

---

## Overview

All SQL execution goes through the Operation Queue. Callers submit work via `Execute` (no rows returned) or `Query` (rows returned) and receive results via awaited Tasks. The caller's thread is never blocked — it awaits the Task while the background consumer processes the Operation.

---

## Execute

**Trigger:** Caller invokes `DatabaseHandle.Execute(sql, parameters?)`

**Steps:**
1. Validate: `sql` must not be null or empty; handle must not be disposed
2. Create an `Operation` with the SQL, parameters, and a `TaskCompletionSource<int>`
3. Write the Operation to the Operation Queue
4. The background consumer:
   a. Prepares the statement via `sqlite3_prepare_v2`
   b. Binds parameters via `sqlite3_bind_*`
   c. Steps via `sqlite3_step` until `SQLITE_DONE`
   d. Sets the result on the TCS: `sqlite3_changes` (rows affected)
   e. Finalizes the statement via `sqlite3_finalize`
5. The caller's `await` resolves with the row count

**On error:** The background consumer catches the native error, wraps it in a `SqlCipherException` (carrying SQLite result code and message), and sets it on the TCS. The awaiting caller receives this exception.

---

## Query

**Trigger:** Caller invokes `DatabaseHandle.Query(sql, parameters?)`

**Steps:**
1. Validate: `sql` must not be null or empty; handle must not be disposed
2. Create an `Operation` with the SQL, parameters, and a `TaskCompletionSource<ResultSet>`
3. Write the Operation to the Operation Queue
4. The background consumer:
   a. Prepares the statement via `sqlite3_prepare_v2`
   b. Binds parameters via `sqlite3_bind_*`
   c. Reads column names from the prepared statement
   d. Steps via `sqlite3_step`, reading each row until `SQLITE_DONE`
   e. Materializes all rows into a `ResultSet` before returning
   f. Sets the `ResultSet` on the TCS
   g. Finalizes the statement via `sqlite3_finalize`
5. The caller's `await` resolves with the `ResultSet`

**On error:** Same as Execute — native error wrapped in `SqlCipherException`, set on TCS.

---

## Parameter Binding

Parameters in SQL are identified by name using the `@name` convention (e.g., `@userId`).

| C# type | SQLite binding |
|---|---|
| `null` | NULL (`sqlite3_bind_null`) |
| `long`, `int`, `short`, `byte`, `bool` | INTEGER (`sqlite3_bind_int64`; bool: 1/0) |
| `double`, `float` | REAL (`sqlite3_bind_double`) |
| `string` | TEXT UTF-8 (`sqlite3_bind_text`) |
| `byte[]`, `ReadOnlyMemory<byte>` | BLOB (`sqlite3_bind_blob`) |

Passing a C# type not in this table → explicit exception before the Operation is queued.

---

## SqlCipherException

A dedicated exception type wrapping native SQLCipher errors.

**Properties:**
| Property | Type | Description |
|---|---|---|
| `SqliteResultCode` | `int` | The raw SQLite result code (e.g., `SQLITE_CONSTRAINT = 19`) |
| `SqliteMessage` | `string` | The error message from `sqlite3_errmsg` |

All native errors surface as `SqlCipherException`. No native error is silently swallowed.

---

## Notes

- There is no statement caching in this version. Each operation prepares and finalizes its statement. This is acceptable for the anticipated workload and avoids cache coherence complexity.
- Multi-statement SQL strings (multiple statements separated by `;`) are not supported in a single `Execute` or `Query` call. The first statement is executed; subsequent statements are ignored. This matches `sqlite3_prepare_v2` behavior and is intentional — callers should submit each statement separately.
