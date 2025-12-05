using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlQueryAnalyzer.Models;

namespace SqlQueryAnalyzer.Visitors;

/// <summary>
/// Visits and extracts all table references from a query
/// </summary>
internal sealed class TableReferenceVisitor : TSqlConcreteFragmentVisitor
{
    public List<QueryTableReference> Tables { get; } = [];

    private JoinType? _currentJoinType;
    private readonly HashSet<string> _cteNames = new(StringComparer.OrdinalIgnoreCase);
    private int _subqueryDepth; // >0 means inside subquery/derived table

    public void SetCteNames(IEnumerable<string> cteNames)
    {
        _cteNames.Clear();
        foreach (var name in cteNames)
        {
            _cteNames.Add(name);
        }
    }

    public override void Visit(ScalarSubquery node)
    {
        _subqueryDepth++;
        // don't traverse children; only mark depth to flag indirect references if needed elsewhere
        // intentionally not calling base
        _subqueryDepth--;
    }

    public override void Visit(QueryDerivedTable node)
    {
        // mark a derived table placeholder but avoid traversing inner query here
        var table = new QueryTableReference
        {
            TableName = "[DerivedTable]",
            Alias = node.Alias?.Value,
            Type = TableReferenceType.DerivedTable,
            JoinType = _currentJoinType,
            DirectReference = _subqueryDepth == 0,
            StartLine = node.StartLine,
            StartColumn = node.StartColumn
        };
        Tables.Add(table);
        // do not call base to avoid inner traversal
    }

    public override void Visit(QualifiedJoin node)
    {
        node.FirstTableReference?.Accept(this);

        _currentJoinType = node.QualifiedJoinType switch
        {
            QualifiedJoinType.Inner => Models.JoinType.Inner,
            QualifiedJoinType.LeftOuter => Models.JoinType.Left,
            QualifiedJoinType.RightOuter => Models.JoinType.Right,
            QualifiedJoinType.FullOuter => Models.JoinType.Full,
            _ => Models.JoinType.Inner
        };

        node.SecondTableReference?.Accept(this);
        _currentJoinType = null;

        node.SearchCondition?.Accept(this);
    }

    public override void Visit(UnqualifiedJoin node)
    {
        node.FirstTableReference?.Accept(this);
        _currentJoinType = Models.JoinType.Cross;
        node.SecondTableReference?.Accept(this);
        _currentJoinType = null;
    }

    public override void Visit(NamedTableReference node)
    {
        var tableName = node.SchemaObject.BaseIdentifier?.Value ?? string.Empty;
        var isCte = _cteNames.Contains(tableName);

        var table = new QueryTableReference
        {
            Database = isCte ? null : node.SchemaObject.DatabaseIdentifier?.Value,
            Schema = isCte ? null : node.SchemaObject.SchemaIdentifier?.Value,
            TableName = tableName,
            Alias = node.Alias?.Value,
            Type = isCte ? TableReferenceType.Cte : TableReferenceType.Table,
            JoinType = _currentJoinType,
            DirectReference = _subqueryDepth == 0,
            StartLine = node.StartLine,
            StartColumn = node.StartColumn
        };

        Tables.Add(table);
    }

    // Prevent traversal into CTE definitions here; handled by CteVisitor
    public override void Visit(CommonTableExpression node)
    {
        // no-op
    }
}
