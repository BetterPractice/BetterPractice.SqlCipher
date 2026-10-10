# Feature: Database Lifecycle

> Covers creation, opening, and disposal of a DatabaseHandle.
> See `/spec/interfaces/database-handle.md` for the API contract.

---

## Overview

A database exists in one of three states: it has never been created, it is open, or it has been disposed. This feature governs the transitions between those states. A database is created encrypted (keyed by a Passphrase or a Raw Key) or, through a separate entry point, unencrypted; and in WAL or rollback journal mode (ADR 008). Both choices are fixed at creation and recorded in the file.

---

## Creating a Database

**Trigger:** Caller invokes one of
- `DatabaseHandle.Create(path, passphrase, cipherConfiguration?, journalMode?)`
- `DatabaseHandle.Create(path, rawKey, cipherConfiguration?, journalMode?)`
- `DatabaseHandle.CreateUnencrypted(path, journalMode?)`

**Preconditions:**
- `path` refers to a location where a file does not already exist
- The directory containing `path` exists
- `passphrase` is non-null and non-empty, or `rawKey` is exactly 32 bytes (no key for `CreateUnencrypted`)

**Steps:**
1. Validate parameters; throw immediately if any precondition is violated — before any native call
2. Open the SQLite database file at `path` via the Native Library (this creates the file)
3. Encrypted only: apply the key via `PRAGMA key = '...'` (Passphrase) or `PRAGMA key = "x'...'"` (Raw Key)
4. Encrypted only: apply Cipher Configuration via PRAGMAs:
   - `PRAGMA cipher_page_size = <PageSize>`
   - `PRAGMA kdf_iter = <KdfIterations>`
   - `PRAGMA cipher_kdf_algorithm = <KdfAlgorithm>`
   - `PRAGMA cipher_hmac_algorithm = <HmacAlgorithm>`
5. Set the journal mode: `PRAGMA journal_mode = WAL` or `PRAGMA journal_mode = DELETE` (rollback), and verify the PRAGMA reports the requested mode
6. Write the first page by executing `CREATE TABLE IF NOT EXISTS _bp_meta (k TEXT PRIMARY KEY, v TEXT)` — this forces SQLCipher to initialize the encryption header
7. Start the Operation Queue background task
8. Return the open `DatabaseHandle`

**Notes:**
- Step 6 uses a sentinel table to force database file initialization. The name `_bp_meta` is reserved and must not be used by consumers. For an unencrypted database it also marks the file as created by this library.
- `CreateUnencrypted` is a separate method, not a null passphrase, so that a missing configuration value cannot silently produce a plaintext file (ADR 008).
- The table `_bp_migrations` is also reserved for the future migrations feature and must not be used by consumers.
- If any step fails after file creation, the partially-created file is deleted before the exception is surfaced.

---

## Opening a Database

**Trigger:** Caller invokes one of
- `DatabaseHandle.Open(path, passphrase)`
- `DatabaseHandle.Open(path, rawKey)`
- `DatabaseHandle.OpenUnencrypted(path)`

**Preconditions:**
- `path` refers to an existing file
- `passphrase` is non-null and non-empty, or `rawKey` is exactly 32 bytes (no key for `OpenUnencrypted`)

**Steps:**
1. Validate parameters; throw immediately if any precondition is violated
2. Open the SQLite database file at `path` via the Native Library
3. Encrypted only: apply the key via `PRAGMA key = '...'` or `PRAGMA key = "x'...'"`
4. Verify the file is readable by executing `SELECT count(*) FROM sqlite_master` — if this fails, the key is incorrect, the file is encrypted when opened as unencrypted (or the reverse), or the file is not a SQLite database; the error says which family of cause applies
5. Read the journal mode: `PRAGMA journal_mode` — `wal` and `delete` are accepted and recorded in `JournalMode`; anything else means the database was not created by this library, and an explicit error is thrown
6. Start the Operation Queue background task
7. Return the open `DatabaseHandle`

**Notes:**
- CipherConfiguration is not re-applied on open; it was baked into the file at creation. SQLCipher reads it from the database header.
- The library never switches the journal mode of an existing database.
- A file created with a Raw Key does not open with a Passphrase, nor the reverse; both fail at step 4.

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
