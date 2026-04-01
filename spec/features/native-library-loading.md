# Feature: Native Library Loading

> Covers how the SQLCipher Native Library is located and loaded at runtime.
> See ADR 003 (`/spec/decisions/003-native-interop.md`) for the LibraryImport decision.

---

## Overview

BetterPractice.SqlCipher uses `LibraryImport` to call into the SQLCipher Native Library. The library name declared in `LibraryImport` attributes must match the filename conventions of the bundled native binary across all supported platforms.

---

## Library Name Convention

The native library is referenced by the logical name `"sqlcipher"` in all `LibraryImport` declarations. The .NET runtime resolves this to the platform-appropriate filename:

| Platform | Resolved filename |
|---|---|
| Linux | `libsqlcipher.so` |
| macOS | `libsqlcipher.dylib` |
| Windows | `sqlcipher.dll` |

The runtime applies the `lib` prefix and platform extension automatically when the logical name has no extension. This is standard .NET P/Invoke behavior.

---

## NativeLibrary Resolution Order

.NET resolves native library references in this order:
1. Application directory
2. `runtimes/<rid>/native/` within the NuGet package (standard RID-based layout)
3. System library paths (PATH / LD_LIBRARY_PATH / DYLD_LIBRARY_PATH)

The runtime packages place the native binary at `runtimes/<rid>/native/` which is the standard location. No custom `NativeLibrary.SetDllImportResolver` callback is required unless resolution fails in practice.

---

## Build: Compiling SQLCipher from Amalgamation

The native binary for each runtime package is compiled from the SQLite amalgamation with SQLCipher patches. The amalgamation source (`sqlite3.c`) is checked into the repository under `native/src/`. Compilation is performed as part of the build pipeline for each target RID.

Required SQLCipher compile-time defines:

| Define | Value | Purpose |
|---|---|---|
| `SQLITE_HAS_CODEC` | (defined) | Enables SQLCipher encryption layer |
| `SQLITE_TEMP_STORE` | `2` | Store temp tables in memory (SQLCipher requirement) |
| `SQLCIPHER_CRYPTO_OPENSSL` or `SQLCIPHER_CRYPTO_CC` | (defined) | Crypto backend (OpenSSL on Linux/Windows; CommonCrypto on macOS/iOS) |

Additional SQLite compile-time options may be added; any addition requires an ADR.

---

## Out of Scope

- Dynamic library path configuration at runtime (library location is fixed by NuGet package layout)
- Supporting system-installed SQLCipher instead of the bundled binary
- Multiple SQLCipher versions or dynamic version selection
