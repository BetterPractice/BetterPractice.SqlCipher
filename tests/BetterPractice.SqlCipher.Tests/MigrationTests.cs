namespace BetterPractice.SqlCipher.Tests;

[TestFixture]
public sealed class MigrationTests
{
    private string? _dbPath;

    [TearDown]
    public void TearDown()
    {
        if (_dbPath is not null) TestHelpers.DeleteTempDb(_dbPath);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IMigration Make(string id, string up, string? down = null) =>
        new LambdaMigration(id, up, down);

    private sealed class LambdaMigration : IMigration
    {
        private readonly string _up;
        private readonly string? _down;

        public LambdaMigration(string id, string up, string? down)
        {
            Id   = id;
            _up   = up;
            _down = down;
        }

        public string Id { get; }

        public void Up(MigrationContext context)   => context.Execute(_up);
        public void Down(MigrationContext context)
        {
            if (_down is null) throw new NotSupportedException($"Migration {Id} has no Down.");
            context.Execute(_down);
        }
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Test]
    public async Task Migrate_FreshDatabase_RunsAllMigrations()
    {
        var (db, path) = await TestHelpers.OpenTempDb();
        _dbPath = path;
        await using (db)
        {
            var migrations = new IMigration[]
            {
                Make("001-create-t1", "CREATE TABLE t1 (id INTEGER PRIMARY KEY)"),
                Make("002-create-t2", "CREATE TABLE t2 (id INTEGER PRIMARY KEY)"),
            };

            await db.Migrate(migrations);

            var r1 = await db.Query("SELECT name FROM sqlite_master WHERE type='table' AND name='t1'");
            var r2 = await db.Query("SELECT name FROM sqlite_master WHERE type='table' AND name='t2'");
            Assert.That(r1.Count, Is.EqualTo(1));
            Assert.That(r2.Count, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Migrate_RecordsAppliedIds()
    {
        var (db, path) = await TestHelpers.OpenTempDb();
        _dbPath = path;
        await using (db)
        {
            await db.Migrate(new IMigration[]
            {
                Make("001-create-t",  "CREATE TABLE t (x INTEGER)"),
                Make("002-add-col-y", "ALTER TABLE t ADD COLUMN y INTEGER"),
            });

            var rows = await db.Query("SELECT id FROM _bp_migrations ORDER BY rowid");
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows[0]["id"].AsText(), Is.EqualTo("001-create-t"));
            Assert.That(rows[1]["id"].AsText(), Is.EqualTo("002-add-col-y"));
        }
    }

    [Test]
    public async Task Migrate_AlreadyApplied_SkipsMigrations()
    {
        var (db, path) = await TestHelpers.OpenTempDb();
        _dbPath = path;
        await using (db)
        {
            var m1 = Make("001-create-t", "CREATE TABLE t (x INTEGER)");
            await db.Migrate(new[] { m1 });

            // Run again — should not throw "table already exists"
            Assert.DoesNotThrowAsync(() => db.Migrate(new[] { m1 }));
        }
    }

    [Test]
    public async Task Migrate_PartialFailure_DoesNotRecordVersion()
    {
        var (db, path) = await TestHelpers.OpenTempDb();
        _dbPath = path;
        await using (db)
        {
            var good = Make("001-good", "CREATE TABLE t (x INTEGER)");
            var bad  = Make("002-bad",  "THIS IS NOT VALID SQL !!!");

            try { await db.Migrate(new[] { good, bad }); }
            catch (MigrationException) { /* expected */ }

            // Only the successful migration is recorded
            var rows = await db.Query("SELECT id FROM _bp_migrations");
            Assert.That(rows.Count, Is.EqualTo(1));
            Assert.That(rows[0]["id"].AsText(), Is.EqualTo("001-good"));
        }
    }

    [Test]
    public async Task Rollback_AppliesDownMigration()
    {
        var (db, path) = await TestHelpers.OpenTempDb();
        _dbPath = path;
        await using (db)
        {
            var m1 = Make("001-create-t", "CREATE TABLE t (x INTEGER)", "DROP TABLE t");
            var m2 = Make("002-add-col",  "ALTER TABLE t ADD COLUMN y INTEGER");

            await db.Migrate(new[] { m1, m2 });

            // Roll back everything after m1 (i.e. m2 gets rolled back).
            // But m2 has no Down — we only supply m1 here as the rollback catalogue.
            // Instead roll back everything after "001-create-t" which is just "002-add-col".
            // SQLite doesn't support DROP COLUMN in older versions, so we add y and then
            // just verify the rollback mechanism tracks the id correctly.

            // We provide the full catalogue to Rollback; it will call Down only for
            // migrations applied after the target id, in reverse order.
            // Since 002-add-col has no Down in our definition, supply it with a no-op.
            var m2WithDown = Make("002-add-col", "ALTER TABLE t ADD COLUMN y INTEGER",
                /* no-op down — SQLite doesn't support DROP COLUMN */ "SELECT 1");

            await db.Rollback("001-create-t", new[] { m1, m2WithDown });

            // 002-add-col should no longer be recorded
            var rows = await db.Query("SELECT id FROM _bp_migrations");
            Assert.That(rows.Count, Is.EqualTo(1));
            Assert.That(rows[0]["id"].AsText(), Is.EqualTo("001-create-t"));
        }
    }
}
