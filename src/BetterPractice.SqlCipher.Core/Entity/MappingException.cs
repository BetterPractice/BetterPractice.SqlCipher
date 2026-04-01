namespace BetterPractice.SqlCipher;

/// <summary>
/// Thrown when row-to-entity conversion fails — e.g., a required column is absent
/// from the result set, or a value cannot be converted to the declared property type.
/// </summary>
public sealed class MappingException : Exception
{
    internal MappingException(string entityTypeName, string columnName, string message, Exception? inner = null)
        : base(message, inner)
    {
        EntityTypeName = entityTypeName;
        ColumnName     = columnName;
    }

    public string EntityTypeName { get; }
    public string ColumnName { get; }
}
