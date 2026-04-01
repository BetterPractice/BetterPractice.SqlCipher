namespace BetterPractice.SqlCipher;

/// <summary>
/// The underlying SQLite storage type of a <see cref="Value"/>.
/// </summary>
public enum SqliteType
{
    Null    = 5,
    Integer = 1,
    Real    = 2,
    Text    = 3,
    Blob    = 4
}
