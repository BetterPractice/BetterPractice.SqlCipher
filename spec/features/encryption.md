# Feature: Encryption and Key Management

> Covers passphrase application, key verification, and rekeying.
> See `/spec/interfaces/database-handle.md` and `/spec/interfaces/cipher-configuration.md`.

---

## Overview

Databases managed by BetterPractice.SqlCipher are encrypted using SQLCipher unless created through the explicit unencrypted entry point (see `database-lifecycle.md`). The key is either a Passphrase, run through the KDF, or a Raw Key, applied as is (ADR 008). It is provided by the caller at creation and open time, never stored, logged, or retained after use. Rekeying is supported as an explicit, expensive operation.

---

## Passphrase Application

The Passphrase is applied to the SQLite connection immediately after opening the file, before any other operation, using:

```sql
PRAGMA key = 'passphrase';
```

SQLCipher derives the encryption key from the Passphrase using the KDF Algorithm and KDF Iterations specified in the CipherConfiguration. On Open, SQLCipher reads these parameters from the database header — they do not need to be respecified by the library.

**The Passphrase string is discarded after the PRAGMA is applied.** It is not stored in any field. If the string was provided as a parameter, the library makes no attempt to zero the memory (this is a .NET managed string; zeroing is not reliably achievable without `SecureString` or pinned memory, which are out of scope for this version).

---

## Raw Key Application

A Raw Key is exactly 32 bytes. It is applied as SQLCipher's blob literal:

```sql
PRAGMA key = "x'<64 hexadecimal digits>'";
```

SQLCipher uses the bytes directly as the page encryption key and skips the KDF. The per-page HMAC key is still derived, from the raw key and the file's salt, with SQLCipher's fixed two-iteration fast KDF; `KdfIterations` and `KdfAlgorithm` in the CipherConfiguration therefore have no effect on the cost of a raw-key open. Hexadecimal needs no escaping. A key of any other length is rejected before any native call. The byte array is the caller's; the library does not zero it and does not retain it.

A database created with a Raw Key stores a different key than one created with a Passphrase of the same bytes, so the two open paths are not interchangeable for one file.

---

## Key Verification on Open

SQLCipher does not fail immediately on an incorrect key — it fails when the first data page is read. After applying the key, the library verifies it explicitly:

```sql
SELECT count(*) FROM sqlite_master;
```

If this query returns an error (SQLite result code `SQLITE_NOTADB` or similar), the Passphrase is incorrect. The library closes the connection and throws a `SqlCipherException` with a clear message indicating authentication failure — it does not surface the raw SQLite error code alone.

---

## Rekeying

**Trigger:** Caller invokes `DatabaseHandle.Rekey(newPassphrase)` or `DatabaseHandle.Rekey(newRawKey)`

Rekeying changes the key on an existing encrypted database. SQLCipher rewrites every page of the database file during this operation. Either form of key may replace either form: the file's cipher configuration is unchanged.

**Steps:**
1. Validate: `newPassphrase` is non-null and non-empty, or `newRawKey` is 32 bytes; handle is not disposed and `IsEncrypted` is true (an Unencrypted Database cannot be rekeyed)
2. Submit an Operation to the Operation Queue containing:
   ```sql
   PRAGMA rekey = 'newPassphrase';        -- or
   PRAGMA rekey = "x'<64 hex digits>'";
   ```
3. The background consumer executes the PRAGMA
4. On success, the new Passphrase is now active — the old Passphrase will not open the database
5. On failure, the database remains encrypted with the original Passphrase; the exception is surfaced to the caller

**Notes:**
- Rekeying is serialized through the Operation Queue like all other operations. No reads or writes can occur concurrently with a rekey.
- Rekeying is expensive — proportional to database file size. Callers should be aware this may take significant time for large databases.
- There is no partial rekey state — SQLCipher either completes the rekey or rolls back to the original key.
- The new Passphrase is discarded after the PRAGMA is applied, same as the original Passphrase.

---

## Cipher Configuration at Creation

The CipherConfiguration PRAGMAs are applied in this order immediately after the key PRAGMA, before the journal mode is set:

```sql
PRAGMA cipher_page_size = <PageSize>;
PRAGMA kdf_iter = <KdfIterations>;
PRAGMA cipher_kdf_algorithm = <KdfAlgorithm>;
PRAGMA cipher_hmac_algorithm = <HmacAlgorithm>;
```

These PRAGMAs must be applied before the first data is written. Once the database is initialized (first page written), the cipher configuration is baked into the file header and cannot be changed by PRAGMAs alone.

---

## Out of Scope

- Zeroing the Passphrase string in managed memory (requires pinned memory / `SecureString`; deferred)
- Cipher configuration migration (changing cipher config on an existing database requires a `VACUUM INTO` a new file; this is not implemented)
- SQLCipher 3 compatibility mode
