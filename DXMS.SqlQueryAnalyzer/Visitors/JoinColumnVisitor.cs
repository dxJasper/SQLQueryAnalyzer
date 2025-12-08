using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DXMS.SqlQueryAnalyzer.Visitors;

/// <summary>
/// Visits and extracts all columns from JOIN ON clauses
/// </summary>
internal sealed class JoinColumnVisitor : TSqlConcreteFragmentVisitor
{
    public List<ColumnReference> Columns { get; } = [];

    private bool _inJoinCondition;

    public override void Visit(QualifiedJoin node)
    {
        node.FirstTableReference?.Accept(this);
        node.SecondTableReference?.Accept(this);

        if (node.SearchCondition is null)
        {
            return;
        }

        _inJoinCondition = true;
        node.SearchCondition.Accept(this);
        _inJoinCondition = false;
    }

    public override void Visit(ColumnReferenceExpression node)
    {
        if (!_inJoinCondition)
        {
            base.Visit(node);
            return;
        }

        var column = ColumnReferenceFactory.Create(node, alias: null, ColumnUsageType.Join);
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
