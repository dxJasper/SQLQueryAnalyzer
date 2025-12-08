using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlQueryAnalyzer.Models;

namespace SqlQueryAnalyzer.Visitors;

/// <summary>
/// Visits and extracts subqueries
/// </summary>
internal sealed class SubQueryVisitor(SqlQueryAnalyzerService analyzer, bool analyzeNestedQueries = true)
    : TSqlConcreteFragmentVisitor
{
    private int _depth;

    public List<SubQueryInfo> SubQueries { get; } = [];

    public override void Visit(QueryDerivedTable node)
    {
        if (_depth > 0)
        {
            var queryText = SqlQueryAnalyzerService.GetFragmentText(node.QueryExpression);

            SubQueries.Add(new SubQueryInfo
            {
                Alias = node.Alias?.Value,
                QueryText = queryText,
                Type = SubQueryType.DerivedTable,
                InnerAnalysis = analyzeNestedQueries && !string.IsNullOrWhiteSpace(queryText)
                    ? analyzer.Analyze(queryText)
                    : null,
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
            InnerAnalysis = analyzeNestedQueries && !string.IsNullOrWhiteSpace(queryText)
                ? analyzer.Analyze(queryText)
                : null,
            StartLine = node.StartLine,
            StartColumn = node.StartColumn
        });
    }

    public override void Visit(ExistsPredicate node)
    {
        var queryText = SqlQueryAnalyzerService.GetFragmentText(node.Subquery.QueryExpression);

        SubQueries.Add(new SubQueryInfo
        {
            QueryText = queryText,
            Type = SubQueryType.ExistsSubquery,
            InnerAnalysis = analyzeNestedQueries && !string.IsNullOrWhiteSpace(queryText)
                ? analyzer.Analyze(queryText)
                : null,
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
                InnerAnalysis = analyzeNestedQueries && !string.IsNullOrWhiteSpace(queryText)
                    ? analyzer.Analyze(queryText)
                    : null,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            });
        }

        base.Visit(node);
    }
}
