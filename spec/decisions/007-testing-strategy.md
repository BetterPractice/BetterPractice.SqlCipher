# ADR 007 — Testing Strategy

**Status:** Accepted
**Date:** 2026-03-30
**Author:** Holly Schilling

---

## Context

BetterPractice.SqlCipher wraps a native library and enforces WAL mode on all databases. In-memory SQLite databases are not viable because WAL mode requires a real file on disk — `PRAGMA journal_mode=WAL` silently fails on in-memory databases, which would cause the library's own WAL verification check to reject them. All tests must run against real SQLCipher database files.

---

## Decision

### Test Framework: NUnit

NUnit is the test framework for `BetterPractice.SqlCipher.Tests`. Its `[TestFixture]` / `[SetUp]` / `[TearDown]` lifecycle maps cleanly to integration tests that need database setup and teardown per test or per fixture.

### Database Isolation: Temp File Per Test

Each test that requires a database creates a unique temp file via `Path.GetTempFileName()` and deletes it in `[TearDown]`. This provides full isolation — no cross-test state pollution.

Tests that share a read-only database fixture (e.g., testing multiple fetch expressions against a pre-populated database) may create one database in `[OneTimeSetUp]` and delete it in `[OneTimeTearDown]`, provided no test in the fixture writes to the shared database.

### Native Binary: MSBuild Content Copy

The test project references the native binary for the developer's local RID as a `Content` item that is copied to the build output directory. The `Compile.targets` native build step deposits the compiled binary to a known location that the test project's `Content` reference points to.

No environment variables or custom `NativeLibrary.SetDllImportResolver` callbacks are required — standard .NET library resolution finds the binary in the output directory.

### Test Coverage Areas

| Area | Approach |
|---|---|
| Database creation | Temp file; verify WAL active, verify wrong key rejected |
| Database open | Existing temp file; correct key, wrong key, missing file |
| Execute / Query | Parameter binding for all supported types, empty result sets, error cases |
| Transactions | Commit path, rollback on exception, `AggregateException` on double failure |
| Migrations | Apply pending, skip applied, rollback, duplicate ID detection, missing Down definition |
| Rekeying | Rekey then reopen with new key; verify old key rejected |
| Entity operations | Auto-PK assignment, upsert, fetch all, fetch with expression, delete by ID, delete by expression |
| Query builder | SELECT with WHERE/JOIN/ORDER BY/LIMIT, INSERT, UPDATE, DDL roundtrip |
| Disposal | `DisposeAsync` drains queue; `Dispose` rejects subsequent operations |

### CI Prerequisites

CI must run the `Compile.targets` native build step before `dotnet test`. The CI pipeline is responsible for ensuring the appropriate C compiler is available on the runner for the target RID.

---

## Consequences

**Enables:**
- Tests exercise the real SQLCipher code path end-to-end — no mocking of the native layer
- Full isolation between tests via temp files
- NUnit's fixture lifecycle supports both per-test and per-fixture database setup patterns

**Constrains:**
- Tests cannot run without a compiled native binary present
- CI must provision a C compiler and run the native build step before running tests
- Test execution is slightly slower than in-memory due to file I/O, but acceptable for an integration test suite

---

## Supersedes / Superseded by

None.
