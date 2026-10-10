namespace BetterPractice.SqlCipher;

/// <summary>
/// How SQLite records changes so a transaction can be rolled back (ADR 008). Chosen at creation,
/// recorded in the file, and verified on open; the library never switches the mode of an existing file.
/// </summary>
public enum JournalMode
{
    /// <summary>
    /// Write-ahead logging: cheaper commits, with <c>-wal</c> and <c>-shm</c> sidecar files that exist
    /// until the last connection closes cleanly. The default.
    /// </summary>
    Wal,

    /// <summary>
    /// The rollback journal (SQLite <c>DELETE</c> mode): the database is a single file at all times,
    /// for files that are copied between media, backed up as opaque blobs, or opened from read-only storage.
    /// </summary>
    Rollback,
}
