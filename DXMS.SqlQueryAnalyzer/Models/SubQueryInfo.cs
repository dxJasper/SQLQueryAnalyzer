namespace DXMS.SqlQueryAnalyzer.Models;

/// <summary>
/// Represents a subquery
/// </summary>
public sealed class SubQueryInfo
{
    public string? Alias { get; init; }
    public required string QueryText { get; init; }
    public SubQueryType Type { get; init; }
    public QueryAnalysisResult? InnerAnalysis { get; init; }
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
}
