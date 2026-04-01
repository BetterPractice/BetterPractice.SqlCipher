namespace BetterPractice.SqlCipher;

/// <summary>
/// Exception wrapping a native SQLCipher error.
/// All native errors surface as SqlCipherException — none are silently swallowed.
/// </summary>
public sealed class SqlCipherException : Exception
{
    internal SqlCipherException(int resultCode, string sqliteMessage)
        : base($"SQLCipher error {resultCode}: {sqliteMessage}")
    {
        SqliteResultCode = resultCode;
        SqliteMessage    = sqliteMessage;
    }

    internal SqlCipherException(int resultCode, string sqliteMessage, string context)
        : base($"SQLCipher error {resultCode} ({context}): {sqliteMessage}")
    {
        SqliteResultCode = resultCode;
        SqliteMessage    = sqliteMessage;
    }

    /// <summary>The raw SQLite result code (e.g., SQLITE_CONSTRAINT = 19).</summary>
    public int SqliteResultCode { get; }

    /// <summary>The error message from sqlite3_errmsg.</summary>
    public string SqliteMessage { get; }
}
