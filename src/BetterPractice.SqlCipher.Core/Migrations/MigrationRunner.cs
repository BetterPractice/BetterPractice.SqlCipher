using BetterPractice.SqlCipher.Internal;

namespace BetterPractice.SqlCipher;

/// <summary>
/// Internal implementation of migration apply and rollback.
/// Called from DatabaseHandle via the Operation Queue — always runs on the background task.
/// </summary>
internal static class MigrationRunner
{
    private const string CreateTableSql =
        "CREATE TABLE IF NOT EXISTS _bp_migrations (" +
        "id TEXT PRIMARY KEY, " +
        "applied_at TEXT NOT NULL DEFAULT (datetime('now')))";

    internal static void Migrate(DatabaseConnection conn, IReadOnlyList<IMigration> migrations)
    {
        ValidateNoDuplicateIds(migrations);

        conn.Execute(CreateTableSql);

        var applied = LoadAppliedIds(conn);
        var context = new MigrationContext(conn);

        foreach (var migration in migrations)
        {
            if (applied.Contains(migration.Id))
                continue;

            conn.Execute("BEGIN");
            try
            {
                migration.Up(context);
                conn.Execute("INSERT INTO _bp_migrations (id) VALUES (@id)",
                    new Dictionary<string, object?> { ["id"] = migration.Id });
                conn.Execute("COMMIT");
            }
            catch (Exception ex)
            {
                TryRollback(conn);
                throw new MigrationException(migration.Id, MigrationPhase.Up,
                    $"Migration '{migration.Id}' failed during Up.", ex);
            }
        }
    }

    internal static void Rollback(DatabaseConnection conn, string targetId, IReadOnlyList<IMigration> migrations)
    {
        conn.Execute(CreateTableSql);

        var appliedOrdered = LoadAppliedIdsOrdered(conn);

        if (!appliedOrdered.Contains(targetId))
            throw new MigrationException(targetId, MigrationPhase.RollbackLookup,
                $"Migration '{targetId}' has not been applied and cannot be used as a rollback target.");

        // All migrations applied after targetId, in reverse order
        var toRollback = appliedOrdered
            .SkipWhile(id => id != targetId)
            .Skip(1)
            .Reverse()
            .ToList();

        var migrationById = migrations.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var context = new MigrationContext(conn);

        foreach (var id in toRollback)
        {
            if (!migrationById.TryGetValue(id, out var migration))
                throw new MigrationException(id, MigrationPhase.RollbackLookup,
                    $"Migration '{id}' is recorded as applied but was not found in the supplied migrations list. " +
                    "Cannot roll back without the migration definition.");

            conn.Execute("BEGIN");
            try
            {
                migration.Down(context);
                conn.Execute("DELETE FROM _bp_migrations WHERE id = @id",
                    new Dictionary<string, object?> { ["id"] = id });
                conn.Execute("COMMIT");
            }
            catch (Exception ex)
            {
                TryRollback(conn);
                throw new MigrationException(id, MigrationPhase.Down,
                    $"Migration '{id}' failed during Down.", ex);
            }
        }
    }

    private static HashSet<string> LoadAppliedIds(DatabaseConnection conn)
    {
        var result = conn.Query("SELECT id FROM _bp_migrations");
        return result.Select(row => row[0].AsText()).ToHashSet(StringComparer.Ordinal);
    }

    private static List<string> LoadAppliedIdsOrdered(DatabaseConnection conn)
    {
        var result = conn.Query("SELECT id FROM _bp_migrations ORDER BY rowid ASC");
        return result.Select(row => row[0].AsText()).ToList();
    }

    private static void ValidateNoDuplicateIds(IReadOnlyList<IMigration> migrations)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in migrations)
        {
            if (!seen.Add(m.Id))
                throw new MigrationException(m.Id, MigrationPhase.Up,
                    $"Duplicate migration Id '{m.Id}' found in the supplied migrations list.");
        }
    }

    private static void TryRollback(DatabaseConnection conn)
    {
        try { conn.Execute("ROLLBACK"); } catch { /* best effort */ }
    }
}
