using System.Runtime.InteropServices;

namespace BetterPractice.SqlCipher;

/// <summary>
/// A single typed field within a <see cref="Row"/>, carrying its SQLite storage type.
/// Null is an explicit value — not a missing field.
/// </summary>
public readonly struct Value
{
    private readonly SqliteType _type;
    private readonly long _integer;
    private readonly double _real;
    private readonly string? _text;
    private readonly byte[]? _blob;

    internal static readonly Value Null = new(SqliteType.Null);

    private Value(SqliteType type) => _type = type;

    internal Value(long integer)
    {
        _type = SqliteType.Integer;
        _integer = integer;
    }

    internal Value(double real)
    {
        _type = SqliteType.Real;
        _real = real;
    }

    internal Value(string text)
    {
        _type = SqliteType.Text;
        _text = text;
    }

    internal Value(byte[] blob)
    {
        _type = SqliteType.Blob;
        _blob = blob;
    }

    /// <summary>The underlying SQLite storage type of this value.</summary>
    public SqliteType SqliteType => _type;

    /// <summary>True if this value is SQL NULL.</summary>
    public bool IsNull => _type == SqliteType.Null;

    /// <summary>Returns the value as a 64-bit integer. Throws if the type is not Integer.</summary>
    public long AsInt64()
    {
        ThrowIfNot(SqliteType.Integer);
        return _integer;
    }

    /// <summary>Returns the value as a 32-bit integer. Throws if the type is not Integer or the value is out of range.</summary>
    public int AsInt32()
    {
        ThrowIfNot(SqliteType.Integer);
        checked { return (int)_integer; }
    }

    /// <summary>Returns the value as a boolean (0 = false, non-zero = true). Throws if the type is not Integer.</summary>
    public bool AsBool()
    {
        ThrowIfNot(SqliteType.Integer);
        return _integer != 0;
    }

    /// <summary>Returns the value as a double. Throws if the type is not Real.</summary>
    public double AsDouble()
    {
        ThrowIfNot(SqliteType.Real);
        return _real;
    }

    /// <summary>Returns the value as a string. Throws if the type is not Text or the value is NULL.</summary>
    public string AsText()
    {
        ThrowIfNot(SqliteType.Text);
        return _text!;
    }

    /// <summary>Returns the value as a read-only byte span. Throws if the type is not Blob.</summary>
    public ReadOnlyMemory<byte> AsBlob()
    {
        ThrowIfNot(SqliteType.Blob);
        return _blob!;
    }

    private void ThrowIfNot(SqliteType expected)
    {
        if (_type != expected)
            throw new InvalidOperationException(
                $"Cannot read a {_type} value as {expected}.");
    }

    public override string ToString() => _type switch
    {
        SqliteType.Null    => "NULL",
        SqliteType.Integer => _integer.ToString(),
        SqliteType.Real    => _real.ToString("G"),
        SqliteType.Text    => _text!,
        SqliteType.Blob    => $"BLOB[{_blob!.Length}]",
        _                  => "Unknown"
    };
}
