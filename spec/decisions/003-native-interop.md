# ADR 003 — Native Interop Strategy

**Status:** Accepted
**Date:** 2026-03-30
**Author:** Holly Schilling

---

## Context

BetterPractice.SqlCipher calls into the SQLCipher Native Library (a compiled C binary) from managed C# code. .NET provides two mechanisms for declaring native function imports:

- **`[DllImport]`** — the legacy runtime-interpreted P/Invoke attribute, available since .NET Framework. Works everywhere but generates marshalling code at runtime, is not AOT-safe without annotation, and does not benefit from source generation.

- **`[LibraryImport]`** — a source-generated P/Invoke attribute introduced in .NET 7. A Roslyn source generator emits the marshalling code at compile time. AOT-compatible, trim-safe, and produces better-performing interop with no runtime reflection.

ADO.NET compatibility is explicitly out of scope (see domain model). A custom P/Invoke layer is required.

---

## Decision

All native function imports must use **`[LibraryImport]`** on `partial static` classes within the internal P/Invoke Layer.

This establishes .NET 8 as the minimum supported runtime (consistent with the "modern toolchains" target stated in CLAUDE.md).

The P/Invoke Layer is internal. No `LibraryImport` declaration or any SQLite/SQLCipher native type is part of the public API surface.

---

## Consequences

**Enables:**
- AOT compilation compatibility — required for iOS target
- Trim-safe interop — no runtime reflection on marshalling paths
- Source-generated marshalling is inspectable and debuggable
- Performance: no runtime delegate wrapping for each native call

**Constrains:**
- Minimum .NET version is .NET 8 (LibraryImport is .NET 7+; .NET 8 is chosen as the LTS anchor)
- All P/Invoke declarations must be on `partial static` classes — this is a compiler requirement for LibraryImport
- String marshalling must be declared explicitly on each import (e.g., `[MarshalAs(UnmanagedType.LPUTF8Str)]` or `StringMarshalling = StringMarshalling.Utf8`) — there is no implicit default

---

## Supersedes / Superseded by

None.
