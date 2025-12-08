namespace DXMS.SqlQueryAnalyzer.Models;

/// <summary>
/// Complete analysis result of a SQL query
/// </summary>
public sealed class QueryAnalysisResult
{
    public required string OriginalQuery { get; init; }
    public List<string> ParseErrors { get; init; } = [];
    public List<QueryTableReference> Tables { get; init; } = [];
    public List<ColumnReference> SelectColumns { get; init; } = [];
    public List<ColumnReference> PredicateColumns { get; init; } = [];
    public List<ColumnReference> JoinColumns { get; init; } = [];
    public List<ColumnReference> GroupByColumns { get; init; } = [];
    public List<ColumnReference> OrderByColumns { get; init; } = [];
    public List<CteDefinition> CommonTableExpressions { get; init; } = [];
    public List<SubQueryInfo> SubQueries { get; init; } = [];
    public List<ColumnLineage> ColumnLineages { get; init; } = [];
    /// <summary>
    /// The final output columns that the user will receive as query results.
    /// This only includes the SELECT items from the outermost/final query, excluding inner CTEs and subqueries.
    /// </summary>
    public List<ColumnReference> FinalQueryColumns { get; init; } = [];

    public bool HasErrors => ParseErrors.Count > 0;

    /// <summary>
    /// All unique schemas referenced in the query
    /// </summary>
    public IEnumerable<string> Schemas => Tables
        .Where(t => !string.IsNullOrEmpty(t.Schema))
        .Select(t => t.Schema!)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// All columns grouped by their usage type
    /// </summary>
    public ILookup<ColumnUsageType, ColumnReference> ColumnsByUsage =>
        SelectColumns
            .Concat(PredicateColumns)
            .Concat(JoinColumns)
            .Concat(GroupByColumns)
            .Concat(OrderByColumns)
            .ToLookup(c => c.UsageType);
}
