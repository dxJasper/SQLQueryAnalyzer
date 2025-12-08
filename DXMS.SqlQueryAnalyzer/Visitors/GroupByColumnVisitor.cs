using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DXMS.SqlQueryAnalyzer.Visitors;

/// <summary>
/// Visits and extracts all columns from GROUP BY clauses
/// </summary>
internal sealed class GroupByColumnVisitor : TSqlConcreteFragmentVisitor
{
    public List<ColumnReference> Columns { get; } = [];

    private bool _inGroupByClause;
    private int _cteDepth;

    public override void Visit(WithCtesAndXmlNamespaces node)
    {
        _cteDepth++;
        base.Visit(node);
        _cteDepth--;
    }

    public override void Visit(CommonTableExpression node)
    {
        // Skip CTE definitions completely 
        // Do NOT call base.Visit(node)
    }

    public override void Visit(GroupByClause node)
    {
        if (_cteDepth > 0)
        {
            return; // Skip GROUP BY inside CTEs
        }

        _inGroupByClause = true;
        base.Visit(node);
    }

    public override void Visit(ExpressionGroupingSpecification node)
    {
        if (!_inGroupByClause || _cteDepth > 0)
        {
            base.Visit(node);
            return;
        }

        if (node.Expression is ColumnReferenceExpression colRef)
        {
            Columns.Add(ColumnReferenceFactory.Create(colRef, alias: null, ColumnUsageType.GroupBy));
        }
        else
        {
            // Expression in GROUP BY - extract all column references
            var innerVisitor = new GroupByExpressionColumnVisitor();
            node.Expression.Accept(innerVisitor);
            Columns.AddRange(innerVisitor.Columns);
        }
    }

    public override void Visit(ColumnReferenceExpression node)
    {
        if (!_inGroupByClause || _cteDepth > 0)
        {
            base.Visit(node);
        }
    }

    private sealed class GroupByExpressionColumnVisitor : TSqlConcreteFragmentVisitor
    {
        public List<ColumnReference> Columns { get; } = [];
        public override void Visit(ColumnReferenceExpression node)
        {
            var column = ColumnReferenceFactory.Create(node, alias: null, ColumnUsageType.GroupBy);
            Columns.Add(column);
            base.Visit(node);
        }
    }
}
