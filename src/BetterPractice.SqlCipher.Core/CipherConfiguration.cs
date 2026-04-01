namespace BetterPractice.SqlCipher;

/// <summary>
/// The key derivation function used to derive the encryption key from the Passphrase.
/// </summary>
public enum KdfAlgorithm
{
    Pbkdf2HmacSha1   = 1,
    Pbkdf2HmacSha256 = 2,
    Pbkdf2HmacSha512 = 3
}

/// <summary>
/// The algorithm used by SQLCipher to compute per-page authentication codes.
/// </summary>
public enum HmacAlgorithm
{
    Sha1   = 1,
    Sha256 = 2,
    Sha512 = 3
}

/// <summary>
/// Immutable value object capturing all SQLCipher encryption parameters for a database.
/// Applied at Database Creation only — cannot be changed on an existing database.
/// </summary>
public sealed class CipherConfiguration
{
    /// <summary>
    /// Default configuration: 4096-byte pages, PBKDF2-HMAC-SHA512, 256,000 iterations, HMAC-SHA512.
    /// Matches SQLCipher 4 defaults.
    /// </summary>
    public static readonly CipherConfiguration Default = new();

    /// <summary>
    /// Initializes a CipherConfiguration with explicit values.
    /// </summary>
    /// <param name="pageSize">Must be a power of two between 512 and 65536. Default: 4096.</param>
    /// <param name="kdfAlgorithm">Key derivation function. Default: PBKDF2-HMAC-SHA512.</param>
    /// <param name="kdfIterations">KDF iteration count. Must be greater than zero. Default: 256000.</param>
    /// <param name="hmacAlgorithm">Per-page HMAC algorithm. Default: SHA512.</param>
    public CipherConfiguration(
        int pageSize         = 4096,
        KdfAlgorithm kdfAlgorithm   = KdfAlgorithm.Pbkdf2HmacSha512,
        int kdfIterations    = 256_000,
        HmacAlgorithm hmacAlgorithm = HmacAlgorithm.Sha512)
    {
        if (!IsPowerOfTwo(pageSize) || pageSize < 512 || pageSize > 65536)
            throw new ArgumentOutOfRangeException(nameof(pageSize),
                "Page size must be a power of two between 512 and 65536.");

        if (kdfIterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(kdfIterations),
                "KDF iteration count must be greater than zero.");

        PageSize      = pageSize;
        KdfAlgorithm  = kdfAlgorithm;
        KdfIterations = kdfIterations;
        HmacAlgorithm = hmacAlgorithm;
    }

    /// <summary>SQLite storage page size in bytes. Fixed at database creation.</summary>
    public int PageSize { get; }

    /// <summary>Key derivation function algorithm.</summary>
    public KdfAlgorithm KdfAlgorithm { get; }

    /// <summary>Number of KDF iterations. Higher values increase brute-force resistance.</summary>
    public int KdfIterations { get; }

    /// <summary>Per-page HMAC algorithm.</summary>
    public HmacAlgorithm HmacAlgorithm { get; }

    internal string KdfAlgorithmPragmaValue => KdfAlgorithm switch
    {
        KdfAlgorithm.Pbkdf2HmacSha1   => "PBKDF2_HMAC_SHA1",
        KdfAlgorithm.Pbkdf2HmacSha256 => "PBKDF2_HMAC_SHA256",
        KdfAlgorithm.Pbkdf2HmacSha512 => "PBKDF2_HMAC_SHA512",
        _                             => throw new InvalidOperationException($"Unknown KdfAlgorithm: {KdfAlgorithm}")
    };

    internal string HmacAlgorithmPragmaValue => HmacAlgorithm switch
    {
        HmacAlgorithm.Sha1   => "HMAC_SHA1",
        HmacAlgorithm.Sha256 => "HMAC_SHA256",
        HmacAlgorithm.Sha512 => "HMAC_SHA512",
        _                    => throw new InvalidOperationException($"Unknown HmacAlgorithm: {HmacAlgorithm}")
    };

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}
