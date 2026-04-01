namespace BetterPractice.SqlCipher;

/// <summary>
/// A single record within a <see cref="ResultSet"/>.
/// Accessible by column index or column name.
/// </summary>
public sealed class Row : IReadOnlyList<Value>
{
    private readonly string[] _columnNames;
    private readonly Value[] _values;
    private readonly Dictionary<string, int> _nameIndex;

    internal Row(string[] columnNames, Value[] values)
    {
        _columnNames = columnNames;
        _values = values;
        _nameIndex = new Dictionary<string, int>(columnNames.Length, StringComparer.Ordinal);
        for (int i = 0; i < columnNames.Length; i++)
            _nameIndex[columnNames[i]] = i;
    }

    /// <summary>Number of columns in this row.</summary>
    public int Count => _values.Length;

    /// <summary>Access a value by zero-based column index.</summary>
    public Value this[int index] => _values[index];

    /// <summary>
    /// Access a value by column name. Column name lookup is case-sensitive.
    /// Throws if the column name is not present in this row.
    /// </summary>
    public Value this[string columnName]
    {
        get
        {
            if (!_nameIndex.TryGetValue(columnName, out int index))
                throw new ArgumentException($"Column '{columnName}' not found in row.", nameof(columnName));
            return _values[index];
        }
    }

    /// <summary>
    /// Returns the value for a column by name, or null if the column is not present.
    /// </summary>
    public Value? TryGet(string columnName) =>
        _nameIndex.TryGetValue(columnName, out int index) ? _values[index] : null;

    public IEnumerator<Value> GetEnumerator() =>
        ((IEnumerable<Value>)_values).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}
