namespace DXMS.SqlQueryAnalyzer.Models;

/// <summary>
/// Represents a source column in lineage tracking
/// </summary>
public sealed class SourceColumn
{
    public string? Database { get; init; }
    public string? Schema { get; init; }
    public string? TableName { get; init; }
    public string? TableAlias { get; init; }
    public required string ColumnName { get; init; }

    private string FullyQualifiedName => string.Join(".", new[] { Database, Schema, TableName, ColumnName }.Where(s => !string.IsNullOrEmpty(s)));

    public override string ToString() => FullyQualifiedName;
}