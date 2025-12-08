using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DXMS.SqlQueryAnalyzer.Visitors;

/// <summary>
/// Visits and extracts Common Table Expressions (CTEs)
/// </summary>
internal sealed class CteVisitor(SqlQueryAnalyzerService analyzer) : TSqlConcreteFragmentVisitor
{
    public List<CteDefinition> Ctes { get; } = [];

    public override void Visit(CommonTableExpression node)
    {
        var queryText = SqlQueryAnalyzerService.GetFragmentText(node.QueryExpression);

        var cte = new CteDefinition
        {
            Name = node.ExpressionName?.Value ?? string.Empty,
            ColumnList = node.Columns?.Select(c => c.Value).ToList() ?? [],
            QueryText = queryText,
            InnerAnalysis = !string.IsNullOrWhiteSpace(queryText)
                ? analyzer.Analyze(queryText)
                : null,
            StartLine = node.StartLine,
            StartColumn = node.StartColumn
        };

        Ctes.Add(cte);

        // Don't call base to avoid re-visiting the inner query
    }
}
