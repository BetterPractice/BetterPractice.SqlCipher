namespace BetterPractice.SqlCipher;

/// <summary>
/// A named, typed parameter placeholder for use in expressions and query builders.
/// Call <see cref="Bind"/> to produce a <see cref="ParamBinding"/> at query execution time.
/// </summary>
public sealed class Param<T>
{
    public Param(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    public string Name { get; }

    public ParamBinding Bind(T value) => new(Name, value);
}

/// <summary>
/// A type-erased bound parameter value, passed alongside an expression at query execution time.
/// </summary>
public sealed class ParamBinding
{
    internal ParamBinding(string name, object? value)
    {
        Name  = name;
        Value = value;
    }

    public string Name { get; }
    public object? Value { get; }
}
