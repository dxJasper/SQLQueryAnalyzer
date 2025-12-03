using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlQueryAnalyzer.Models;

namespace SqlQueryAnalyzer.Visitors;

/// <summary>
/// Visits and extracts subqueries
/// </summary>
internal sealed class SubQueryVisitor : TSqlConcreteFragmentVisitor
{
    private readonly SqlQueryAnalyzerService _analyzer;
    private int _depth;
    
    public List<SubQueryInfo> SubQueries { get; } = [];
    
    public SubQueryVisitor(SqlQueryAnalyzerService analyzer)
    {
        _analyzer = analyzer;
    }
    
    public override void Visit(QueryDerivedTable node)
    {
        if (_depth > 0) // Only capture nested subqueries, not top-level
        {
            var queryText = SqlQueryAnalyzerService.GetFragmentText(node.QueryExpression);
            
            SubQueries.Add(new SubQueryInfo
            {
                Alias = node.Alias?.Value,
                QueryText = queryText,
                Type = SubQueryType.DerivedTable,
                InnerAnalysis = !string.IsNullOrWhiteSpace(queryText) ? _analyzer.Analyze(queryText) : null,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            });
        }
        
        _depth++;
        base.Visit(node);
        _depth--;
    }
    
    public override void Visit(ScalarSubquery node)
    {
        var queryText = SqlQueryAnalyzerService.GetFragmentText(node.QueryExpression);
        
        SubQueries.Add(new SubQueryInfo
        {
            QueryText = queryText,
            Type = SubQueryType.ScalarSubquery,
            InnerAnalysis = !string.IsNullOrWhiteSpace(queryText) ? _analyzer.Analyze(queryText) : null,
            StartLine = node.StartLine,
            StartColumn = node.StartColumn
        });
        
        // Don't visit children to avoid double-processing
    }
    
    public override void Visit(ExistsPredicate node)
    {
        var queryText = SqlQueryAnalyzerService.GetFragmentText(node.Subquery.QueryExpression);
        
        SubQueries.Add(new SubQueryInfo
        {
            QueryText = queryText,
            Type = SubQueryType.ExistsSubquery,
            InnerAnalysis = !string.IsNullOrWhiteSpace(queryText) ? _analyzer.Analyze(queryText) : null,
            StartLine = node.StartLine,
            StartColumn = node.StartColumn
        });
    }
    
    public override void Visit(InPredicate node)
    {
        if (node.Subquery is not null)
        {
            var queryText = SqlQueryAnalyzerService.GetFragmentText(node.Subquery.QueryExpression);
            
            SubQueries.Add(new SubQueryInfo
            {
                QueryText = queryText,
                Type = SubQueryType.InSubquery,
                InnerAnalysis = !string.IsNullOrWhiteSpace(queryText) ? _analyzer.Analyze(queryText) : null,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            });
        }
        
        base.Visit(node);
    }
}
