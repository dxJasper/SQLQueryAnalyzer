namespace DXMS.SqlQueryAnalyzer.Models;

/// <summary>
/// Represents a CTE definition
/// </summary>
public sealed class CteDefinition
{
    public required string Name { get; init; }
    public List<string> ColumnList { get; init; } = [];
    public required string QueryText { get; init; }
    public QueryAnalysisResult? InnerAnalysis { get; init; }
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
}