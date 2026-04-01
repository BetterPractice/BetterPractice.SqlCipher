# ADR 006 — Solution Structure

**Status:** Accepted
**Date:** 2026-03-30
**Author:** Holly Schilling

---

## Context

The project requires managed C# code, a Roslyn source generator, per-platform native binaries, NuGet packaging for multiple RIDs, and tests. These concerns have different target frameworks, build requirements, and packaging identities that need to be organized clearly.

---

## Decision

### Single Solution

All projects live in one solution (`BetterPractice.SqlCipher.sln`). The source generator is developed in the same solution as the library — no pack/restore cycle required during development. The generator is referenced by the Core project as an analyzer reference.

### Project Layout

```
SqlCipher-Net/
  BetterPractice.SqlCipher.sln
  src/
    BetterPractice.SqlCipher.Core/
      BetterPractice.SqlCipher.Core.csproj        ← net8.0
    BetterPractice.SqlCipher.Generator/
      BetterPractice.SqlCipher.Generator.csproj   ← netstandard2.0 (Roslyn requirement)
  native/
    src/
      sqlite3.c       ← SQLCipher amalgamation snapshot
      sqlite3.h
      sqlcipher.h
    build/
      Compile.targets ← MSBuild custom target, accepts RuntimeIdentifier parameter
    bin/              ← compiled native binaries (gitignored)
  packaging/
    BetterPractice.SqlCipher.Runtime.csproj       ← single multi-targeting project
  tests/
    BetterPractice.SqlCipher.Tests/
      BetterPractice.SqlCipher.Tests.csproj       ← net8.0
  spec/
  CLAUDE.md
```

### Runtime Packaging — Single Multi-Targeting Project

A single `BetterPractice.SqlCipher.Runtime.csproj` produces one NuGet package per supported RID using MSBuild conditions on `RuntimeIdentifier`. Each pack invocation specifies the target RID; the project sets `PackageId` accordingly:

```xml
<PackageId>BetterPractice.SqlCipher.runtime.$(RuntimeIdentifier)</PackageId>
```

This keeps the packaging definition in one place. Adding a new platform means adding a new condition block, not a new project file.

Supported RIDs (initial):
- `linux-x64`
- `linux-arm64`
- `osx-x64`
- `osx-arm64`
- `win-x64`
- `win-arm64`

### Native Binaries — Gitignored

Compiled native binaries in `native/bin/` are excluded from source control. Developers and CI runners must compile the native library before building or packing. The `Compile.targets` file is the single authoritative definition of how to do this.

Rationale: the SQLite amalgamation is updated infrequently (roughly annually), and once the project reaches stability it will have minimal active development. Committing binaries would add repository bloat for marginal convenience. If this becomes a real friction point, pre-built binaries can be added to `native/bin/` under source control with a clear update convention.

### Generator Target Framework

`BetterPractice.SqlCipher.Generator` targets `netstandard2.0`. This is a hard requirement for Roslyn source generators — they run inside the compiler host. The generator uses the `IIncrementalGenerator` API (see ADR 004).

### Nullable Reference Types

All projects enable nullable reference types (`<Nullable>enable</Nullable>`). This is non-negotiable — it is required for the "no silent failures" principle stated in CLAUDE.md.

---

## Consequences

**Enables:**
- Single `dotnet build` from the solution root builds everything
- Generator changes are reflected immediately without a pack/restore cycle
- Runtime packaging is maintained in one file
- Clean repository without binary files

**Constrains:**
- `dotnet pack` for runtime packages requires specifying `-r <rid>` to select the target platform; CI must invoke pack once per supported RID
- Native binaries must be compiled before tests can run; CI must run `Compile.targets` as a prerequisite step
- Developer machines need a C compiler (Clang on macOS, GCC or Clang on Linux, MSVC or Clang-cl on Windows) for their local platform to run tests locally

---

## Supersedes / Superseded by

None.
