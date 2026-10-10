# ADR 008 — Unencrypted Databases, Selectable Journal Mode, and Raw Key Mode

**Status:** Accepted
**Date:** 2026-10-10
**Author:** Holly Schilling
**Supersedes:** ADR 002 (the statement that WAL mode is always enabled regardless of concurrency model); ADR 007 (the premise that WAL mode is enforced on all databases); the domain model invariant *WAL always on*; the *Raw key bytes* item under *Out of Scope* in the encryption feature
**Superseded by:** —

---

## Context

Version 0.1.0 made three choices that the first consumer, the Better Practice Practice Server, has now run into:

1. **Every database is encrypted with a passphrase.** The consumer's bootstrap database holds only public values and ciphertext under stronger keys, and encrypting the file coupled non-secret data to a master key, which made a legitimate recovery scenario impossible. It needs a plain SQLite file managed through the same API.

2. **The key is always a passphrase run through PBKDF2.** The consumer's main database key is a 32-byte HKDF output from its master key: already uniformly random. Base64-encoding it and running 256,000 PBKDF2 iterations over the result adds a hundred milliseconds or more to every open and stretches a key that cannot be stretched further. SQLCipher supports a raw key (`PRAGMA key = "x'…'"`) that skips the KDF; the library did not expose it.

3. **WAL mode is mandatory.** WAL's benefit, readers not blocking a writer across connections, does not apply to this library's single serialized connection (ADR 002). Its cost is two sidecar files that exist until a clean close. Files that are copied between media, backed up as opaque blobs, or opened from read-only or archival storage want the single-file semantics of the rollback journal. The consumer has three such databases: its bootstrap database, its custody store on removable media, and its monthly audit logs.

The alternative of making `passphrase` optional and treating null as "unencrypted" was considered and rejected: a null or empty value arriving from configuration would then silently create a plaintext database, where today it throws. Plaintext must be an explicit, greppable choice.

---

## Decision

### Unencrypted databases are an explicit, separate entry point

`DatabaseHandle.CreateUnencrypted(path, …)` and `DatabaseHandle.OpenUnencrypted(path)` manage a plain SQLite file through the same handle, queue, query, migration, and entity APIs. No key is applied. `Open` with any key fails on such a file, and `OpenUnencrypted` fails on an encrypted file, both with explicit errors. `Rekey` is unavailable and `IsEncrypted` is false. The existing `Create` and `Open` keep rejecting a null or empty passphrase.

### Journal mode is chosen at creation and verified on open

`JournalMode` has two values, `Wal` and `Rollback` (SQLite's `DELETE` journal). Every creation method takes it as an optional parameter defaulting to `Wal`, so existing callers are unchanged. On open the library reads `PRAGMA journal_mode` from the file, accepts `wal` or `delete`, records the result in the handle's `JournalMode` property, and rejects anything else as not created by this library. The library never switches the journal mode of an existing file.

### Raw key mode skips the KDF

`Create(path, byte[] rawKey, …)`, `Open(path, byte[] rawKey)`, and `Rekey(byte[] newRawKey)` take a 32-byte key and apply it as SQLCipher's blob literal, `PRAGMA key = "x'<64 hex digits>'"`. SQLCipher uses the bytes directly as the page encryption key and derives the per-page HMAC key from them with its fast two-iteration KDF, so an open costs no measurable key derivation. Any other length is rejected before a native call. A database created with a raw key cannot be opened with a passphrase, or the reverse, because the stored key differs.

Raw key mode is the correct choice whenever the caller already holds full-entropy key material. Passphrase mode remains for human-remembered secrets, where the KDF is doing real work.

---

## Consequences

### Enabled

- The consumer's bootstrap database and custody store become single, portable, plain SQLite files, readable by any SQLite tool and safe to copy when closed.
- The consumer's main database opens without a redundant 256,000-iteration KDF over an already-random key.
- Archived files on read-only storage can be opened once a read-only open is added (not in this version).
- In-memory databases become possible in principle, since the WAL requirement that ruled them out (ADR 007) is no longer universal. Not pursued in this version.

### Constrained

- Four creation paths and three open paths replace one of each. The `IsEncrypted` and `JournalMode` properties tell a caller what it holds.
- The on-disk key of a raw-key database differs from a passphrase database with "the same" bytes. Consumers switching must recreate or rekey.
- The choice of journal mode is the caller's responsibility. A consumer that picks `Rollback` for a database with concurrent writers from several processes loses nothing with this library's single connection, but the option exists to be misused elsewhere.
- Unencrypted files are a deliberate hole in a library named for encryption. The separate method names keep that hole visible in every code review.

### Changes to other specifications

- `features/database-lifecycle.md`: the new creation and open paths, journal mode steps, raw key steps.
- `features/encryption.md`: raw key application and rekeying; the out-of-scope item removed.
- `interfaces/database-handle.md`: the new signatures and properties.
- `domain/domain-model.md`: the WAL invariant becomes a journal-mode invariant.
- `GLOSSARY.md`: Journal Mode, Raw Key, WAL Mode (revised), Unencrypted Database.
- ADR 002 and ADR 007 receive *Superseded by* pointers; their bodies are unchanged.
