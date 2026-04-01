namespace BetterPractice.SqlCipher.Tests;

/// <summary>
/// Shared test infrastructure.
/// Each test method should call <see cref="TempDb"/> to get its own isolated database file.
/// Files are cleaned up in TearDown.
/// </summary>
internal static class TestHelpers
{
    private const string TestPassphrase = "test-passphrase-12345";

    /// <summary>Creates a temp file path and returns it. The caller is responsible for deletion.</summary>
    internal static string TempFilePath() =>
        Path.Combine(Path.GetTempPath(), $"sqlcipher_test_{Guid.NewGuid():N}.db");

    /// <summary>Opens a new encrypted database at a temp path and returns the handle + path.</summary>
    internal static async Task<(DatabaseHandle Db, string Path)> OpenTempDb(
        string? passphrase = null,
        CipherConfiguration? config = null)
    {
        string path = TempFilePath();
        var db = await DatabaseHandle.Create(path, passphrase ?? TestPassphrase, config);
        return (db, path);
    }

    /// <summary>Cleans up the database file and its WAL/SHM siblings.</summary>
    internal static void DeleteTempDb(string path)
    {
        foreach (string suffix in new[] { "", "-wal", "-shm" })
        {
            string f = path + suffix;
            if (File.Exists(f)) File.Delete(f);
        }
    }
}
