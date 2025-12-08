using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DXMS.SqlQueryAnalyzer.Visitors;

/// <summary>
/// Visits and extracts all columns from SELECT clauses
/// </summary>
internal sealed class SelectColumnVisitor : TSqlConcreteFragmentVisitor
{
    public List<ColumnReference> Columns { get; } = [];

    private int _subqueryDepth;

    public override void Visit(WithCtesAndXmlNamespaces node)
    {
        // Skip CTE definitions entirely - don't traverse them
        // Only process the main query after CTEs
        base.Visit(node);
    }

    public override void Visit(CommonTableExpression node)
    {
        // Completely skip CTE definition - don't traverse into it at all
        // This prevents collecting SELECT columns from CTE definitions
        // Do NOT call base.Visit(node)
    }

    public override void Visit(QueryDerivedTable node)
    {
        _subqueryDepth++;
        _subqueryDepth--;
    }

    public override void Visit(ScalarSubquery node)
    {
        _subqueryDepth++;
        _subqueryDepth--;
    }

    public override void Visit(SelectScalarExpression node)
    {
        if (_subqueryDepth > 0)
        {
            return;
        }

        var alias = node.ColumnName?.Value;

        if (node.Expression is ColumnReferenceExpression colRef)
        {
            var column = ColumnReferenceFactory.Create(colRef, alias, ColumnUsageType.Select);
            Columns.Add(column);
            return;
        }

        ColumnReference? baseCol = null;
        var innerVisitor = new ExpressionColumnVisitor(ColumnUsageType.Select);
        node.Expression.Accept(innerVisitor);
        if (innerVisitor.Columns.Count > 0)
        {
            baseCol = innerVisitor.Columns[0];
        }

        var exprCol = ColumnReferenceFactory.CreateExpression(node.Expression, alias, ColumnUsageType.Select, baseCol);
        Columns.Add(exprCol);
    }

    public override void Visit(SelectStarExpression node)
    {
        if (_subqueryDepth > 0)
        {
            return;
        }
        Columns.Add(ColumnReferenceFactory.CreateStar(node));
    }

    /// <summary>
    /// Helper visitor to extract column references from expressions
    /// </summary>
    private sealed class ExpressionColumnVisitor(ColumnUsageType usageType) : TSqlConcreteFragmentVisitor
    {
        public List<ColumnReference> Columns { get; } = [];

        public override void Visit(ColumnReferenceExpression node)
        {
            var column = ColumnReferenceFactory.Create(node, alias: null, usageType);
            Columns.Add(column);
            base.Visit(node);
        }
    }
}
