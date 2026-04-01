# Feature: Database Lifecycle

> Covers creation, opening, and disposal of a DatabaseHandle.
> See `/spec/interfaces/database-handle.md` for the API contract.

---

## Overview

A database exists in one of three states: it has never been created, it is open, or it has been disposed. This feature governs the transitions between those states.

---

## Creating a Database

**Trigger:** Caller invokes `DatabaseHandle.Create(path, passphrase, cipherConfiguration?)`

**Preconditions:**
- `path` refers to a location where a file does not already exist
- The directory containing `path` exists
- `passphrase` is non-null and non-empty

**Steps:**
1. Validate parameters; throw immediately if any precondition is violated — before any native call
2. Open the SQLite database file at `path` via the Native Library (this creates the file)
3. Apply the key via `PRAGMA key = '...'`
4. Apply Cipher Configuration via PRAGMAs:
   - `PRAGMA cipher_page_size = <PageSize>`
   - `PRAGMA kdf_iter = <KdfIterations>`
   - `PRAGMA cipher_kdf_algorithm = <KdfAlgorithm>`
   - `PRAGMA cipher_hmac_algorithm = <HmacAlgorithm>`
5. Enable WAL mode: `PRAGMA journal_mode = WAL`
6. Write the first page by executing `CREATE TABLE IF NOT EXISTS _bp_meta (k TEXT PRIMARY KEY, v TEXT)` — this forces SQLCipher to initialize the encryption header
7. Start the Operation Queue background task
8. Return the open `DatabaseHandle`

**Notes:**
- Step 6 uses a sentinel table to force database file initialization. The name `_bp_meta` is reserved and must not be used by consumers.
- The table `_bp_migrations` is also reserved for the future migrations feature and must not be used by consumers.
- If any step fails after file creation, the partially-created file is deleted before the exception is surfaced.

---

## Opening a Database

**Trigger:** Caller invokes `DatabaseHandle.Open(path, passphrase)`

**Preconditions:**
- `path` refers to an existing file
- `passphrase` is non-null and non-empty

**Steps:**
1. Validate parameters; throw immediately if any precondition is violated
2. Open the SQLite database file at `path` via the Native Library
3. Apply the key via `PRAGMA key = '...'`
4. Verify the key is correct by executing `SELECT count(*) FROM sqlite_master` — if this fails, the passphrase is incorrect
5. Verify WAL mode is active: `PRAGMA journal_mode` — if not WAL, throw an explicit error (this database was not created by this library or was misconfigured)
6. Start the Operation Queue background task
7. Return the open `DatabaseHandle`

**Notes:**
- CipherConfiguration is not re-applied on open; it was baked into the file at creation. SQLCipher reads it from the database header.
- A database not in WAL mode is rejected. This library does not silently switch journal modes on an existing database.

---

## Disposing a DatabaseHandle

**Trigger:** Caller invokes `Dispose()` or `DisposeAsync()`

**Steps:**
1. Mark the handle as disposed (reject subsequent operation submissions)
2. Complete the write end of the Operation Queue channel (signal no more work)
3. `DisposeAsync`: await the background task to drain remaining queued operations
4. `Dispose`: do not await drain — in-flight operations receive an exception
5. Close the SQLite connection via the Native Library
6. Release all managed and unmanaged resources

**Notes:**
- `Dispose()` is synchronous and does not drain. Operations that were queued but not yet started receive a disposed exception via their `TaskCompletionSource`.
- `DisposeAsync()` completes the channel and awaits the background task, allowing queued operations to finish before the connection is closed. This is the preferred disposal path.
- Calling `Dispose` or `DisposeAsync` more than once is a no-op after the first call.
