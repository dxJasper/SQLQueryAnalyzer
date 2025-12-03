using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlQueryAnalyzer.Models;

namespace SqlQueryAnalyzer.Visitors;

/// <summary>
/// Visits and extracts Common Table Expressions (CTEs)
/// </summary>
internal sealed class CteVisitor : TSqlConcreteFragmentVisitor
{
    private readonly SqlQueryAnalyzerService _analyzer;
    
    public List<CteDefinition> Ctes { get; } = [];
    
    public CteVisitor(SqlQueryAnalyzerService analyzer)
    {
        _analyzer = analyzer;
    }
    
    public override void Visit(CommonTableExpression node)
    {
        var queryText = SqlQueryAnalyzerService.GetFragmentText(node.QueryExpression);
        
        var cte = new CteDefinition
        {
            Name = node.ExpressionName?.Value ?? string.Empty,
            ColumnList = node.Columns?.Select(c => c.Value).ToList() ?? [],
            QueryText = queryText,
            InnerAnalysis = !string.IsNullOrWhiteSpace(queryText) ? _analyzer.Analyze(queryText) : null,
            StartLine = node.StartLine,
            StartColumn = node.StartColumn
        };
        
        Ctes.Add(cte);
        
        // Don't call base to avoid re-visiting the inner query
    }
}
