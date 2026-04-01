namespace BetterPractice.SqlCipher;

// -------------------------------------------------------------------------
// Column types and constraints for DDL
// -------------------------------------------------------------------------

public enum ColumnType { Integer, Real, Text, Blob }

public abstract class ColumnConstraint
{
    private ColumnConstraint() { }

    public static readonly ColumnConstraint PrimaryKey    = new PrimaryKeyConstraint();
    public static readonly ColumnConstraint AutoIncrement = new AutoIncrementConstraint();
    public static readonly ColumnConstraint NotNull       = new NotNullConstraint();
    public static readonly ColumnConstraint Unique        = new UniqueConstraint();

    public static ColumnConstraint Default(object value)          => new DefaultConstraint(value);
    public static ColumnConstraint References(string table, string column) =>
        new ReferencesConstraint(table, column);

    internal abstract string ToSql();

    private sealed class PrimaryKeyConstraint    : ColumnConstraint { internal override string ToSql() => "PRIMARY KEY"; }
    private sealed class AutoIncrementConstraint : ColumnConstraint { internal override string ToSql() => "AUTOINCREMENT"; }
    private sealed class NotNullConstraint       : ColumnConstraint { internal override string ToSql() => "NOT NULL"; }
    private sealed class UniqueConstraint        : ColumnConstraint { internal override string ToSql() => "UNIQUE"; }

    private sealed class DefaultConstraint : ColumnConstraint
    {
        private readonly object _value;
        internal DefaultConstraint(object value) => _value = value;
        internal override string ToSql() => $"DEFAULT ({SqlLiteral(_value)})";
    }

    private sealed class ReferencesConstraint : ColumnConstraint
    {
        private readonly string _table, _column;
        internal ReferencesConstraint(string table, string column) { _table = table; _column = column; }
        internal override string ToSql() => $"REFERENCES {_table}({_column})";
    }

    private static string SqlLiteral(object value) => value switch
    {
        null         => "NULL",
        string s     => $"'{s.Replace("'", "''")}'",
        bool b       => b ? "1" : "0",
        int or long  => value.ToString()!,
        double or float => value.ToString()!,
        _ => $"'{value}'"
    };
}

// -------------------------------------------------------------------------
// CreateTable
// -------------------------------------------------------------------------

/// <summary>Immutable fluent builder for CREATE TABLE statements.</summary>
public sealed class CreateTable
{
    private readonly string _name;
    private readonly bool _ifNotExists;
    private readonly (string Name, ColumnType Type, ColumnConstraint[] Constraints)[] _columns;

    private CreateTable(
        string name,
        bool ifNotExists,
        (string, ColumnType, ColumnConstraint[])[] columns)
    {
        _name        = name;
        _ifNotExists = ifNotExists;
        _columns     = columns;
    }

    public static CreateTable Named(string name, bool ifNotExists = true) =>
        new(name, ifNotExists, []);

    public CreateTable Column(string name, ColumnType type, params ColumnConstraint[] constraints) =>
        new(_name, _ifNotExists, _columns.Append((name, type, constraints)).ToArray());

    public BuiltQuery Build()
    {
        if (_columns.Length == 0)
            throw new InvalidOperationException("CreateTable requires at least one column.");

        var colDefs = _columns.Select(c =>
        {
            string typeName = c.Type switch
            {
                ColumnType.Integer => "INTEGER",
                ColumnType.Real    => "REAL",
                ColumnType.Text    => "TEXT",
                ColumnType.Blob    => "BLOB",
                _ => throw new ArgumentException($"Unknown ColumnType: {c.Type}")
            };
            string constraints = c.Constraints.Length > 0
                ? " " + string.Join(" ", c.Constraints.Select(x => x.ToSql()))
                : string.Empty;
            return $"{c.Name} {typeName}{constraints}";
        });

        string ifNotExists = _ifNotExists ? "IF NOT EXISTS " : string.Empty;
        string sql = $"CREATE TABLE {ifNotExists}{_name} ({string.Join(", ", colDefs)})";
        return new BuiltQuery(sql, new Dictionary<string, object?>());
    }
}

// -------------------------------------------------------------------------
// AlterTable
// -------------------------------------------------------------------------

/// <summary>
/// Immutable builder for ALTER TABLE statements.
/// Each instance represents exactly one alteration.
/// </summary>
public sealed class AlterTable
{
    private readonly string _sql;

    private AlterTable(string sql) => _sql = sql;

    public static AlterTable Rename(string tableName, string newName) =>
        new($"ALTER TABLE {tableName} RENAME TO {newName}");

    public static AlterTable RenameColumn(string tableName, string columnName, string newName) =>
        new($"ALTER TABLE {tableName} RENAME COLUMN {columnName} TO {newName}");

    public static AlterTable AddColumn(string tableName, string columnName, ColumnType type,
        params ColumnConstraint[] constraints)
    {
        string typeName = type switch
        {
            ColumnType.Integer => "INTEGER",
            ColumnType.Real    => "REAL",
            ColumnType.Text    => "TEXT",
            ColumnType.Blob    => "BLOB",
            _ => throw new ArgumentException($"Unknown ColumnType: {type}")
        };
        string constraintsSql = constraints.Length > 0
            ? " " + string.Join(" ", constraints.Select(c => c.ToSql()))
            : string.Empty;
        return new($"ALTER TABLE {tableName} ADD COLUMN {columnName} {typeName}{constraintsSql}");
    }

    public static AlterTable DropColumn(string tableName, string columnName) =>
        new($"ALTER TABLE {tableName} DROP COLUMN {columnName}");

    public BuiltQuery Build() => new(_sql, new Dictionary<string, object?>());
}

// -------------------------------------------------------------------------
// DropTable
// -------------------------------------------------------------------------

public sealed class DropTable
{
    private readonly string _name;
    private readonly bool _ifExists;

    private DropTable(string name, bool ifExists) { _name = name; _ifExists = ifExists; }

    public static DropTable Named(string name, bool ifExists = true) => new(name, ifExists);

    public BuiltQuery Build()
    {
        string clause = _ifExists ? "IF EXISTS " : string.Empty;
        return new BuiltQuery($"DROP TABLE {clause}{_name}", new Dictionary<string, object?>());
    }
}

// -------------------------------------------------------------------------
// CreateIndex
// -------------------------------------------------------------------------

public sealed class CreateIndex
{
    private readonly string _indexName;
    private readonly string? _tableName;
    private readonly bool _unique;
    private readonly bool _ifNotExists;
    private readonly (string Column, SortDirection Direction)[] _columns;

    private CreateIndex(
        string indexName,
        string? tableName,
        bool unique,
        bool ifNotExists,
        (string, SortDirection)[] columns)
    {
        _indexName   = indexName;
        _tableName   = tableName;
        _unique      = unique;
        _ifNotExists = ifNotExists;
        _columns     = columns;
    }

    public static CreateIndex Named(string indexName, bool ifNotExists = true) =>
        new(indexName, null, false, ifNotExists, []);

    public CreateIndex On(string tableName) =>
        new(_indexName, tableName, _unique, _ifNotExists, _columns);

    public CreateIndex Column(string name, SortDirection direction = SortDirection.Ascending) =>
        new(_indexName, _tableName, _unique, _ifNotExists, _columns.Append((name, direction)).ToArray());

    public CreateIndex Unique() =>
        new(_indexName, _tableName, true, _ifNotExists, _columns);

    public BuiltQuery Build()
    {
        if (_tableName is null) throw new InvalidOperationException("CreateIndex requires On(tableName).");
        if (_columns.Length == 0) throw new InvalidOperationException("CreateIndex requires at least one column.");

        string unique      = _unique ? "UNIQUE " : string.Empty;
        string ifNotExists = _ifNotExists ? "IF NOT EXISTS " : string.Empty;
        var cols = _columns.Select(c =>
            c.Direction == SortDirection.Descending ? $"{c.Column} DESC" : c.Column);

        string sql = $"CREATE {unique}INDEX {ifNotExists}{_indexName} ON {_tableName} ({string.Join(", ", cols)})";
        return new BuiltQuery(sql, new Dictionary<string, object?>());
    }
}

// -------------------------------------------------------------------------
// DropIndex
// -------------------------------------------------------------------------

public sealed class DropIndex
{
    private readonly string _name;
    private readonly bool _ifExists;

    private DropIndex(string name, bool ifExists) { _name = name; _ifExists = ifExists; }

    public static DropIndex Named(string name, bool ifExists = true) => new(name, ifExists);

    public BuiltQuery Build()
    {
        string clause = _ifExists ? "IF EXISTS " : string.Empty;
        return new BuiltQuery($"DROP INDEX {clause}{_name}", new Dictionary<string, object?>());
    }
}
