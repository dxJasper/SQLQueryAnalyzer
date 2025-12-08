namespace DXMS.SqlQueryAnalyzer.Models;

/// <summary>
/// Represents a column reference in the query
/// </summary>
public sealed class ColumnReference
{
    public string? TableAlias { get; init; }
    public string? TableName { get; init; }
    public string? Schema { get; init; }
    public required string ColumnName { get; init; }
    public string? Alias { get; init; }
    public ColumnUsageType UsageType { get; init; }
    public string? Expression { get; init; }
    public bool IsAscending { get; init; } = true;
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
    public ColumnKind Kind { get; init; } = ColumnKind.Column;

    /// <summary>
    /// The table/alias this column is associated with
    /// </summary>
    public string? SourceIdentifier => TableAlias ?? TableName;

    private string FullName => string.Join(".", new[] { Schema, TableAlias ?? TableName, ColumnName }.Where(s => !string.IsNullOrEmpty(s)));

    public override string ToString() => Alias is not null ? $"{FullName} AS {Alias}" : FullName;
}