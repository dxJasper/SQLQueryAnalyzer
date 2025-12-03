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

    public void SetCteNames(IEnumerable<string> cteNames)
    {
        _cteNames.Clear();
        foreach (var name in cteNames)
        {
            _cteNames.Add(name);
        }
    }

    public override void Visit(QualifiedJoin node)
    {
        // Visit first table (left side)
        node.FirstTableReference?.Accept(this);

        // Set join type for second table
        _currentJoinType = node.QualifiedJoinType switch
        {
            QualifiedJoinType.Inner => Models.JoinType.Inner,
            QualifiedJoinType.LeftOuter => Models.JoinType.Left,
            QualifiedJoinType.RightOuter => Models.JoinType.Right,
            QualifiedJoinType.FullOuter => Models.JoinType.Full,
            _ => Models.JoinType.Inner
        };

        // Visit second table (right side)
        node.SecondTableReference?.Accept(this);

        _currentJoinType = null;

        // Visit the search condition
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
            StartLine = node.StartLine,
            StartColumn = node.StartColumn
        };

        Tables.Add(table);
    }

    public override void Visit(SchemaObjectFunctionTableReference node)
    {
        var table = new QueryTableReference
        {
            Database = node.SchemaObject.DatabaseIdentifier?.Value,
            Schema = node.SchemaObject.SchemaIdentifier?.Value,
            TableName = node.SchemaObject.BaseIdentifier?.Value ?? string.Empty,
            Alias = node.Alias?.Value,
            Type = TableReferenceType.TableValuedFunction,
            JoinType = _currentJoinType,
            StartLine = node.StartLine,
            StartColumn = node.StartColumn
        };

        Tables.Add(table);
    }

    public override void Visit(QueryDerivedTable node)
    {
        var table = new QueryTableReference
        {
            TableName = "[DerivedTable]",
            Alias = node.Alias?.Value,
            Type = TableReferenceType.DerivedTable,
            JoinType = _currentJoinType,
            StartLine = node.StartLine,
            StartColumn = node.StartColumn
        };

        Tables.Add(table);

        // Don't call base to avoid visiting the inner query - it's handled by SubQueryVisitor
    }

    // Prevent traversal into CTE definitions - they're handled by CteVisitor
    public override void Visit(CommonTableExpression node)
    {
        // Don't traverse into CTE definitions
    }
}
