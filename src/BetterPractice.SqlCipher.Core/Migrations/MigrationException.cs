namespace BetterPractice.SqlCipher;

public enum MigrationPhase { Up, Down, RollbackLookup }

/// <summary>
/// Thrown when a migration fails during Up, Down, or rollback lookup.
/// </summary>
public sealed class MigrationException : Exception
{
    internal MigrationException(string migrationId, MigrationPhase phase, string message, Exception? inner = null)
        : base(message, inner)
    {
        MigrationId = migrationId;
        Phase       = phase;
    }

    /// <summary>The Id of the migration that failed.</summary>
    public string MigrationId { get; }

    /// <summary>The phase during which the failure occurred.</summary>
    public MigrationPhase Phase { get; }
}
