# Feature: Migrations

> Covers schema migration definition, application, and rollback.
> See `/spec/interfaces/migration.md` for the API contract.
> See `/spec/GLOSSARY.md` for term definitions.

---

## Overview

Migrations are discrete, versioned units of schema change. Each migration has an `Up` path (apply the change) and a `Down` path (reverse it). Migrations are applied in order and tracked in the reserved `_bp_migrations` table. It is safe to call `Migrate` on every application launch — already-applied migrations are skipped.

---

## The `_bp_migrations` Table

Created automatically on first call to `Migrate` if it does not exist:

```sql
CREATE TABLE IF NOT EXISTS _bp_migrations (
    id       TEXT PRIMARY KEY,
    applied_at TEXT NOT NULL DEFAULT (datetime('now'))
)
```

`id` is the stable string identifier from `IMigration.Id`. `applied_at` records when the migration was applied, in UTC ISO-8601 format.

---

## Applying Migrations

**Trigger:** Caller invokes `DatabaseHandle.Migrate(IReadOnlyList<IMigration> migrations)`

**Steps:**
1. Ensure `_bp_migrations` table exists (CREATE TABLE IF NOT EXISTS)
2. Load all applied migration IDs into a `HashSet<string>`
3. Iterate `migrations` in the order provided by the caller
4. For each migration whose `Id` is not in the applied set:
   a. Begin a transaction
   b. Call `migration.Up(context)` where `context` is a `MigrationContext`
   c. Insert the migration `Id` into `_bp_migrations`
   d. Commit the transaction
   e. On any exception: roll back the transaction, surface the exception, stop — do not attempt further migrations
5. Return when all pending migrations have been applied or an error occurs

**Ordering:** Migrations are applied strictly in the order they appear in the supplied list. The caller is responsible for providing them in the correct order. There is no automatic ordering by ID or timestamp.

**Idempotency:** Safe to call on every launch. Migrations already recorded in `_bp_migrations` are skipped regardless of position in the list.

**All migration operations run through the Operation Queue** — `Migrate` submits a single composite Operation that runs all pending migrations serially. No other operations can interleave during migration.

---

## Rolling Back

**Trigger:** Caller invokes `DatabaseHandle.Rollback(string targetId, IReadOnlyList<IMigration> migrations)`

**Steps:**
1. Load `_bp_migrations` ordered by `rowid ASC` (insertion order)
2. Validate that `targetId` exists in the applied set — throw `MigrationException` if not found
3. Identify all migrations applied *after* `targetId` (i.e. the slice from `targetId` exclusive to the end, reversed)
4. For each migration ID in the reversed slice:
   a. Look up the corresponding `IMigration` in the supplied list by `Id`
   b. If not found in the supplied list: throw `MigrationException` — unlike SqlCipherKit, silent skipping is not permitted here; a missing migration definition during rollback is an error
   c. Begin a transaction
   d. Call `migration.Down(context)`
   e. Delete the migration `Id` from `_bp_migrations`
   f. Commit the transaction
   g. On any exception: roll back the transaction, surface the exception, stop
5. `targetId` itself is **not** rolled back — it remains applied

**Note:** `Rollback` rolls back to the state where `targetId` was the most recently applied migration. To roll back `targetId` itself, pass the ID of the migration before it.

---

## `MigrationContext`

Provided to `Up` and `Down` methods. Exposes a subset of database operations suitable for migrations — both DDL and DML to support data migrations.

The context runs within the migration's transaction. All operations submitted through it execute synchronously on the background task (not re-enqueued) — they are part of the same transaction as the migration record insertion.

**Available operations:**
- `Execute(string sql, ...)` — raw SQL execution (DDL and DML)
- `Query(string sql, ...)` — raw SQL query, returns `ResultSet`
- DDL builder methods: `CreateTable`, `AlterTable`, `DropTable`, `CreateIndex`, `DropIndex`
- DML builder methods: `Insert`, `Update`

**Not available on `MigrationContext`:**
- Entity operations (`Save`, `Fetch`, `Delete`) — the schema may be in a transitional state during migration; typed entity operations are not safe
- `Rekey` — key changes are not permitted within a migration transaction
- Nested transactions — `MigrationContext` already runs within a transaction; attempting to `BEGIN` inside a migration is an error

---

## Error Handling

All migration failures surface as `MigrationException` with:
- `MigrationId` — the ID of the migration that failed
- `InnerException` — the underlying `SqlCipherException` or other exception
- A clear message indicating whether the failure was in `Up`, `Down`, or during rollback lookup

Partial migration state (some migrations applied, then failure) is explicit — the applied set in `_bp_migrations` reflects exactly which migrations succeeded. The caller can inspect the exception and retry after resolving the issue.

---

## Conventions

**Migration ID format:** Callers are free to use any stable string. The recommended convention is `NNN-short-description` (e.g. `"001-create-users"`, `"002-add-score-column"`). IDs must be unique within a migration list — duplicate IDs are an error detected before any migration runs.

**`Down` is required:** `IMigration.Down` must be implemented. A no-op body is permitted but must be a conscious choice. There is no optional `Down`.

**DDL in transactions:** SQLite supports transactional DDL (unlike most other databases). `CREATE TABLE`, `ALTER TABLE`, and `DROP TABLE` are transactional and will be rolled back if the migration fails. This is a feature — schema changes are atomic with their `_bp_migrations` record.
