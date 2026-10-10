# Domain Model

> Core entities, relationships, and invariants for BetterPractice.SqlCipher.
> All terminology is defined in `/spec/GLOSSARY.md`.

---

## Entities

### DatabaseHandle

The root entity. Represents an open, encrypted SQLite database and is the sole point of access for all operations.

**Owns:**
- One SQLite connection (internal, never exposed)
- One Operation Queue (`Channel<Operation>`)
- One background processing task (the queue consumer)

**Invariants:**
- A DatabaseHandle is either open or disposed — there is no intermediate state
- All operations submitted after disposal are rejected with an explicit error
- The journal mode (WAL or rollback) is chosen at creation, recorded in the file, and verified on open (ADR 008)
- The Operation Queue is unbounded
- The background task is the only code that touches the SQLite connection directly

**Lifecycle:**
```
Created → Open → [Operations submitted and processed] → Disposed
```

---

### CipherConfiguration

A value object capturing all SQLCipher encryption parameters for a database. Immutable once constructed.

**Fields:**
| Field | Type | Default | Constraints |
|---|---|---|---|
| PageSize | int | 4096 | Power of 2, 512–65536 |
| KdfAlgorithm | KdfAlgorithm | PBKDF2-HMAC-SHA512 | Enumerated |
| KdfIterations | int | 256000 | > 0 |
| HmacAlgorithm | HmacAlgorithm | SHA512 | Enumerated |

**Invariants:**
- PageSize must be a power of two between 512 and 65536 inclusive
- KdfIterations must be a positive integer
- CipherConfiguration is applied only at Database Creation; it cannot be changed on an existing database without migration

---

### Operation

An internal value object representing a unit of work queued for execution. Not part of the public API.

**Fields:**
- SQL statement (string)
- Parameters (ordered collection of named Values)
- A `TaskCompletionSource` carrying the result back to the caller

**Invariants:**
- Every Operation has exactly one TaskCompletionSource
- The Operation Queue consumer sets either a result or an exception on the TaskCompletionSource — never neither, never both
- An Operation is never requeued after execution, regardless of outcome

---

### ResultSet

The output of a query Operation. An ordered sequence of Rows.

**Invariants:**
- A ResultSet may be empty (zero rows) — this is not an error
- Row order matches the order returned by SQLite
- A ResultSet is fully materialized before being returned to the caller — there are no lazy cursors exposed in the public API

---

### Row

A single record within a ResultSet. An ordered, named collection of Values.

**Invariants:**
- Column names within a Row are unique (as SQLite guarantees for well-formed queries)
- Column count and names are fixed for all Rows within a given ResultSet

---

### Value

A single typed field within a Row.

**Underlying SQLite types:** NULL, INTEGER, REAL, TEXT, BLOB

**Invariants:**
- A Value always carries its SQLite type — callers can inspect the type before converting
- Null is an explicit Value (type NULL), not a missing value or C# `null` on a reference type

---

## Relationships

```
DatabaseHandle
  │
  ├── owns ──────────────► CipherConfiguration  (set at creation, read-only thereafter)
  │
  ├── owns ──────────────► Operation Queue
  │                            │
  │                            └── contains ──► Operation (0..*)
  │                                                │
  │                                                └── produces ──► ResultSet (0..1)
  │                                                                      │
  │                                                                      └── contains ──► Row (0..*)
  │                                                                                          │
  │                                                                                          └── contains ──► Value (1..*)
  │
  └── owns ──────────────► Background Task (the queue consumer)
```

---

## State Machine: DatabaseHandle Lifecycle

```
             ┌─────────────┐
             │   Created   │
             └──────┬──────┘
                    │ Open() / Create()
                    ▼
             ┌─────────────┐
             │    Open     │◄─── Operations submitted and awaited
             └──────┬──────┘
                    │ Dispose()
                    ▼
             ┌─────────────┐
             │  Disposed   │  — all further operations rejected
             └─────────────┘
```

**Error transitions:** If the underlying SQLite connection is lost or corrupted, the DatabaseHandle transitions to Disposed and propagates the error to any in-flight Operation via its TaskCompletionSource.

---

## Invariants Across the Model

1. **No silent failures.** Every error surfaces as an exception on the caller's awaited task. The Operation Queue consumer never swallows exceptions.
2. **No shared mutable state.** The SQLite connection is owned exclusively by the background task. No other code path accesses it.
3. **Journal mode is creation-time only.** A DatabaseHandle sets the journal mode (WAL by default, rollback on request) when it creates a file and verifies on open that the file is in one of those two modes. The library never switches the journal mode of an existing file (ADR 008).
4. **Key material never persisted.** The Passphrase string or Raw Key bytes are used to key the database and then released. They are never written to disk, logged, or stored in any field on any object. An Unencrypted Database has no key.
5. **CipherConfiguration is creation-time only.** Once a database is created, its CipherConfiguration is baked into the file. The library does not attempt to read or verify the stored configuration on open — SQLCipher handles that implicitly via the key verification page.
