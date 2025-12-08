namespace DXMS.SqlQueryAnalyzer.Models;

/// <summary>
/// Represents a table reference in the query
/// </summary>
public sealed class QueryTableReference
{
    public string? Database { get; init; }
    public string? Schema { get; init; }
    public required string TableName { get; init; }
    public string? Alias { get; init; }
    public TableReferenceType Type { get; init; }
    public JoinType? JoinType { get; init; }
    public bool DirectReference { get; init; } // true when referenced in top-level query

    public List<ColumnReference> SelectColumns { get; init; } = [];
    public List<ColumnReference> JoinColumns { get; init; } = [];
    public List<ColumnReference> PredicateColumns { get; init; } = [];

    public string FullName => string.Join(".",
        new[] { Database, Schema, TableName }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>
    /// The identifier used to reference this table (alias if available, otherwise table name)
    /// </summary>
    public string ReferenceIdentifier => Alias ?? TableName;

    public override string ToString() => Alias is not null ? $"{FullName} AS {Alias}" : FullName;
}