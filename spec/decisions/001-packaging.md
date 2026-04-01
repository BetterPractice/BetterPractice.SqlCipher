# ADR 001 — NuGet Package Structure

**Status:** Accepted
**Date:** 2026-03-30
**Author:** Holly Schilling

---

## Context

BetterPractice.SqlCipher bundles platform-specific native binaries (the compiled SQLCipher Native Library) alongside managed C# code. NuGet supports several strategies for distributing native binaries alongside managed code. The choice affects consumer ergonomics, package download size, AOT/trim compatibility, and the ease of adding new platforms later.

Three options were evaluated:

- **Option A:** Single package with all native binaries under `runtimes/`
- **Option B:** Core managed package + separate runtime packages per RID
- **Option C:** Metapackage + core package + runtime packages (as used by SQLitePCLRaw)

The platform matrix is expected to grow, and iOS support requires AOT compilation, making "ship everything in one package" increasingly costly.

---

## Decision

Use **Option B**: a core managed package plus separate per-RID runtime packages.

Package identities:
- `BetterPractice.SqlCipher.core` — managed C# assembly only, no native binaries
- `BetterPractice.SqlCipher.runtime.<rid>` — native binary for a specific RID

Initial runtime packages:
- `BetterPractice.SqlCipher.runtime.linux-x64`
- `BetterPractice.SqlCipher.runtime.linux-arm64`
- `BetterPractice.SqlCipher.runtime.osx-x64`
- `BetterPractice.SqlCipher.runtime.osx-arm64`
- `BetterPractice.SqlCipher.runtime.win-x64`
- `BetterPractice.SqlCipher.runtime.win-arm64`

iOS and Android runtime packages are deferred until AOT build pipeline is established.

The managed package is named `.core` (not `BetterPractice.SqlCipher`) to preserve the option of adding a metapackage (Option C) later without renaming or breaking existing consumers.

---

## Consequences

**Enables:**
- Consumers include only the runtime packages they need — no unnecessary binaries
- AOT/trim-safe: each runtime package contains only one platform's binary
- Adding a new platform is a new package — no version bump to existing packages required
- Clean migration path to Option C: publish a `BetterPractice.SqlCipher` metapackage later with no changes to `.core` or runtime packages

**Constrains:**
- Consumers must reference both `BetterPractice.SqlCipher.core` and at least one runtime package; forgetting the runtime package produces a `DllNotFoundException` at runtime
- Three package identities to version-coordinate instead of one (mitigated by keeping all packages in one repository on the same version)

---

## Supersedes / Superseded by

None.
