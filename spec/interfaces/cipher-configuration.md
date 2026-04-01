# Interface: CipherConfiguration

> Value object capturing SQLCipher encryption parameters.
> See `/spec/GLOSSARY.md` for term definitions.

---

## Overview

`CipherConfiguration` is an immutable value object that groups the SQLCipher-specific encryption parameters for a database. It is supplied at Database Creation and cannot be changed afterward.

---

## Construction

```
CipherConfiguration(
    int pageSize = 4096,
    KdfAlgorithm kdfAlgorithm = KdfAlgorithm.Pbkdf2HmacSha512,
    int kdfIterations = 256000,
    HmacAlgorithm hmacAlgorithm = HmacAlgorithm.Sha512)
```

**Validation (enforced in constructor):**
- `pageSize` must be a power of two in the range [512, 65536]
- `kdfIterations` must be greater than zero

---

## Static Factory

### `CipherConfiguration.Default`

Returns a `CipherConfiguration` with all default values (see table below). This is the configuration used when `null` is passed to `DatabaseHandle.Create`.

```
static CipherConfiguration Default { get; }
```

---

## Properties

| Property | Type | Default | Constraints |
|---|---|---|---|
| `PageSize` | `int` | 4096 | Power of 2, 512–65536 |
| `KdfAlgorithm` | `KdfAlgorithm` | `Pbkdf2HmacSha512` | See `KdfAlgorithm` enum |
| `HmacAlgorithm` | `HmacAlgorithm` | `Sha512` | See `HmacAlgorithm` enum |
| `KdfIterations` | `int` | 256000 | > 0 |

All properties are read-only after construction.

---

## Enumerations

### `KdfAlgorithm`

| Value | SQLCipher PRAGMA value | Notes |
|---|---|---|
| `Pbkdf2HmacSha1` | `PBKDF2_HMAC_SHA1` | Legacy; not recommended for new databases |
| `Pbkdf2HmacSha256` | `PBKDF2_HMAC_SHA256` | |
| `Pbkdf2HmacSha512` | `PBKDF2_HMAC_SHA512` | **Default** |

### `HmacAlgorithm`

| Value | SQLCipher PRAGMA value | Notes |
|---|---|---|
| `Sha1` | `HMAC_SHA1` | Legacy; not recommended for new databases |
| `Sha256` | `HMAC_SHA256` | |
| `Sha512` | `HMAC_SHA512` | **Default** |

---

## Notes

- `CipherConfiguration` is a creation-time parameter only. There is no mechanism to change the cipher configuration of an existing database through this library — doing so requires migrating data to a new database file, which is out of scope.
- The SQLCipher defaults documented here match SQLCipher 4. Databases created with SQLCipher 3 defaults (SHA1, lower iteration counts) are not a target use case.
