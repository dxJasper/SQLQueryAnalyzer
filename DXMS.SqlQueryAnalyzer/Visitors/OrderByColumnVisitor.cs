using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DXMS.SqlQueryAnalyzer.Visitors;

/// <summary>
/// Visits and extracts all columns from ORDER BY clauses
/// </summary>
internal sealed class OrderByColumnVisitor : TSqlConcreteFragmentVisitor
{
    public List<ColumnReference> Columns { get; } = [];

    public override void Visit(ExpressionWithSortOrder node)
    {
        var isAscending = node.SortOrder != SortOrder.Descending;

        if (node.Expression is ColumnReferenceExpression colRef)
        {
            var column = ColumnReferenceFactory.Create(colRef, alias: null, ColumnUsageType.OrderBy, isAscending);
            Columns.Add(column);
        }
        else
        {
            var innerVisitor = new OrderByExpressionColumnVisitor(isAscending);
            node.Expression.Accept(innerVisitor);
            Columns.AddRange(innerVisitor.Columns);
        }
    }

    private sealed class OrderByExpressionColumnVisitor(bool isAscending) : TSqlConcreteFragmentVisitor
    {
        public List<ColumnReference> Columns { get; } = [];
        public override void Visit(ColumnReferenceExpression node)
        {
            var column = ColumnReferenceFactory.Create(node, alias: null, ColumnUsageType.OrderBy, isAscending);
            Columns.Add(column);
            base.Visit(node);
        }
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
