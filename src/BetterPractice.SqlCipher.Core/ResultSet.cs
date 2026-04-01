namespace BetterPractice.SqlCipher;

/// <summary>
/// An ordered, fully materialized sequence of <see cref="Row"/>s returned by a query.
/// An empty ResultSet (Count == 0) is a valid, non-error result.
/// </summary>
public sealed class ResultSet : IReadOnlyList<Row>
{
    private readonly Row[] _rows;

    internal static readonly ResultSet Empty = new([], []);

    internal ResultSet(string[] columnNames, Row[] rows)
    {
        ColumnNames = columnNames;
        _rows = rows;
    }

    /// <summary>Ordered list of column names, consistent across all rows.</summary>
    public IReadOnlyList<string> ColumnNames { get; }

    /// <summary>Number of rows in this result set.</summary>
    public int Count => _rows.Length;

    /// <summary>Access a row by zero-based index.</summary>
    public Row this[int index] => _rows[index];

    public IEnumerator<Row> GetEnumerator() =>
        ((IEnumerable<Row>)_rows).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}
