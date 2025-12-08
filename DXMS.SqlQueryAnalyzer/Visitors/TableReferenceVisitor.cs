using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DXMS.SqlQueryAnalyzer.Visitors;

/// <summary>
/// Visits and extracts all table references from a query
/// </summary>
internal sealed class TableReferenceVisitor : TSqlConcreteFragmentVisitor
{
    public List<QueryTableReference> Tables { get; } = [];

    private JoinType? _currentJoinType;
    private readonly HashSet<string> _cteNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _insideSubquery = false;

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
        // Set the flag to indicate we're inside a subquery
        var wasInsideSubquery = _insideSubquery;
        _insideSubquery = true;

        // Let base visitor handle traversal
        base.Visit(node);

        // Restore the previous state
        _insideSubquery = wasInsideSubquery;
    }

    public override void Visit(QueryDerivedTable node)
    {
        // Mark a derived table placeholder
        var table = new QueryTableReference
        {
            TableName = "[DerivedTable]",
            Alias = node.Alias?.Value,
            Type = TableReferenceType.DerivedTable,
            JoinType = _currentJoinType,
            DirectReference = !_insideSubquery,
        };
        Tables.Add(table);

        // Set the flag to indicate we're inside a subquery for the derived table content
        var wasInsideSubquery = _insideSubquery;
        _insideSubquery = true;

        // Let base visitor handle traversal
        base.Visit(node);

        // Restore the previous state
        _insideSubquery = wasInsideSubquery;
    }

    public override void Visit(InPredicate node)
    {
        // Visit the expression being tested
        node.Expression?.Accept(this);

        // Handle subquery in IN clause
        if (node.Subquery is not null)
        {
            var wasInsideSubquery = _insideSubquery;
            _insideSubquery = true;

            node.Subquery.Accept(this);

            _insideSubquery = wasInsideSubquery;
        }

        // Visit values if not a subquery
        if (node.Values is null)
        {
            return;
        }

        foreach (var value in node.Values)
        {
            value.Accept(this);
        }
    }

    public override void Visit(ExistsPredicate node)
    {
        // Set the flag to indicate we're inside a subquery
        var wasInsideSubquery = _insideSubquery;
        _insideSubquery = true;

        // Let base visitor handle traversal
        base.Visit(node);

        // Restore the previous state
        _insideSubquery = wasInsideSubquery;
    }

    public override void Visit(QualifiedJoin node)
    {
        node.FirstTableReference?.Accept(this);

        _currentJoinType = node.QualifiedJoinType switch
        {
            QualifiedJoinType.Inner => JoinType.Inner,
            QualifiedJoinType.LeftOuter => JoinType.Left,
            QualifiedJoinType.RightOuter => JoinType.Right,
            QualifiedJoinType.FullOuter => JoinType.Full,
            _ => JoinType.Inner
        };

        node.SecondTableReference?.Accept(this);
        _currentJoinType = null;

        node.SearchCondition?.Accept(this);
    }

    public override void Visit(UnqualifiedJoin node)
    {
        node.FirstTableReference?.Accept(this);
        _currentJoinType = JoinType.Cross;
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
            DirectReference = !_insideSubquery
        };

        Tables.Add(table);
    }

    // Prevent traversal into CTE definitions here; handled by CteVisitor
    public override void Visit(CommonTableExpression node)
    {
        // no-op
    }
}