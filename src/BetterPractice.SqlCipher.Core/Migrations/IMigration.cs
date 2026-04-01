namespace BetterPractice.SqlCipher;

/// <summary>
/// A discrete, versioned schema change with an Up (apply) and Down (reverse) path.
/// </summary>
public interface IMigration
{
    /// <summary>
    /// Stable, unique identifier for this migration.
    /// Recommended format: "NNN-short-description" (e.g. "001-create-users").
    /// Must not change after the migration has been applied to any database.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Applies this migration. Called within a transaction — throw to roll back.
    /// </summary>
    void Up(MigrationContext context);

    /// <summary>
    /// Reverses this migration. Called within a transaction — throw to roll back.
    /// A no-op body is permitted but must be a conscious choice.
    /// </summary>
    void Down(MigrationContext context);
}
