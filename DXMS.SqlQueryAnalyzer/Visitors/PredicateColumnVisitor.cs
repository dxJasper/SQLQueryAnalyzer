using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DXMS.SqlQueryAnalyzer.Visitors;

/// <summary>
/// Visits and extracts all columns from WHERE clauses (predicates)
/// </summary>
internal sealed class PredicateColumnVisitor : TSqlConcreteFragmentVisitor
{
    public List<ColumnReference> Columns { get; } = [];

    private bool _inWhereClause;
    private bool _inHavingClause;

    public override void Visit(WhereClause node)
    {
        _inWhereClause = true;
        base.Visit(node);
        _inWhereClause = false;
    }

    public override void Visit(HavingClause node)
    {
        _inHavingClause = true;
        base.Visit(node);
        _inHavingClause = false;
    }

    public override void Visit(ColumnReferenceExpression node)
    {
        if (!_inWhereClause && !_inHavingClause)
        {
            base.Visit(node);
            return;
        }

        var usageType = _inHavingClause ? ColumnUsageType.Having : ColumnUsageType.Where;
        var column = ColumnReferenceFactory.Create(node, alias: null, usageType);
        Columns.Add(column);

        base.Visit(node);
    }

    public override void Visit(CommonTableExpression node)
    {
        // Don't traverse into CTEs
    }

    public override void Visit(ScalarSubquery node)
    {
        // Don't traverse into scalar subqueries
    }

    public override void Visit(QueryDerivedTable node)
    {
        // Don't traverse into derived table subqueries
    }
}
