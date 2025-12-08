namespace DXMS.SqlQueryAnalyzer.Models;

/// <summary>
/// Represents column lineage - tracking where output columns come from
/// </summary>
public sealed class ColumnLineage
{
    /// <summary>
    /// The output column name (alias or original name)
    /// </summary>
    public required string OutputColumn { get; init; }

    /// <summary>
    /// The output column alias (if any)
    /// </summary>
    public string? OutputAlias { get; init; }

    /// <summary>
    /// Source columns that contribute to this output
    /// </summary>
    public List<SourceColumn> SourceColumns { get; init; } = [];

    /// <summary>
    /// Whether this is a computed/derived column
    /// </summary>
    public bool IsComputed { get; init; }

    /// <summary>
    /// The expression used to compute the column (if applicable)
    /// </summary>
    public string? Expression { get; init; }

    /// <summary>
    /// Type of transformation applied
    /// </summary>
    public TransformationType Transformation { get; init; }
}
