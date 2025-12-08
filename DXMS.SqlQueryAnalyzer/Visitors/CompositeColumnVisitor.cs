using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DXMS.SqlQueryAnalyzer.Visitors;

/// <summary>
/// Single-pass visitor that collects Select, Where/Having, Join, GroupBy, and OrderBy columns.
/// Skips traversing into CTE definitions and subqueries for outer-scope collections where appropriate.
/// </summary>
internal sealed class CompositeColumnVisitor : TSqlConcreteFragmentVisitor
{
    public List<ColumnReference> SelectColumns { get; } = [];
    public List<ColumnReference> PredicateColumns { get; } = [];
    public List<ColumnReference> JoinColumns { get; } = [];
    public List<ColumnReference> GroupByColumns { get; } = [];
    public List<ColumnReference> OrderByColumns { get; } = [];

    private bool _inWhere;
    private bool _inHaving;
    private bool _inGroupBy;
    private int _cteDepth;
    private int _subqueryDepth;
    private bool _inJoinCondition;

    public override void Visit(WithCtesAndXmlNamespaces node)
    {
        _cteDepth++;
        base.Visit(node);
        _cteDepth--;
    }

    public override void Visit(CommonTableExpression node)
    {
        // Skip CTE definitions entirely
        // Do NOT call base.Visit(node)
    }

    public override void Visit(QueryDerivedTable node)
    {
        _subqueryDepth++;
        base.Visit(node);
        _subqueryDepth--;
    }

    public override void Visit(ScalarSubquery node)
    {
        _subqueryDepth++;
        base.Visit(node);
        _subqueryDepth--;
    }

    // SELECT list
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
            SelectColumns.Add(column);
            return;
        }

        ColumnReference? baseColumn = null;
        var innerVisitor = new ExpressionColumnVisitor(ColumnUsageType.Select);
        node.Expression.Accept(innerVisitor);

        if (innerVisitor.Columns.Count > 0)
        {
            baseColumn = innerVisitor.Columns[0];
        }

        var columnReference = ColumnReferenceFactory.CreateExpression(node.Expression, alias, ColumnUsageType.Select, baseColumn);

        SelectColumns.Add(columnReference);
    }

    public override void Visit(SelectStarExpression node)
    {
        if (_subqueryDepth > 0) return;
        SelectColumns.Add(ColumnReferenceFactory.CreateStar(node));
    }

    // WHERE / HAVING
    public override void Visit(WhereClause node)
    {
        _inWhere = true;
        base.Visit(node);
        _inWhere = false;
    }

    public override void Visit(HavingClause node)
    {
        _inHaving = true;
        base.Visit(node);
        _inHaving = false;
    }

    public override void Visit(ColumnReferenceExpression node)
    {
        // Join ON condition columns
        if (_inJoinCondition)
        {
            var joinColumn = ColumnReferenceFactory.Create(node, alias: null, ColumnUsageType.Join);
            JoinColumns.Add(joinColumn);
            base.Visit(node);
            return;
        }

        // Predicate columns (WHERE/HAVING)
        if ((_inWhere || _inHaving) && _subqueryDepth == 0)
        {
            var usage = _inHaving ? ColumnUsageType.Having : ColumnUsageType.Where;
            var predicateColumn = ColumnReferenceFactory.Create(node, alias: null, usage);
            PredicateColumns.Add(predicateColumn);

            base.Visit(node);

            return;
        }

        // GroupBy columns (handled via grouping specs)
        if (_inGroupBy && _cteDepth == 0 && _subqueryDepth == 0)
        {
            var groupColumn = ColumnReferenceFactory.Create(node, alias: null, ColumnUsageType.GroupBy);
            GroupByColumns.Add(groupColumn);
        }

        base.Visit(node);
    }

    // JOINs
    public override void Visit(QualifiedJoin node)
    {
        node.FirstTableReference?.Accept(this);
        node.SecondTableReference?.Accept(this);

        if (node.SearchCondition is null) return;
        _inJoinCondition = true;
        node.SearchCondition.Accept(this);
        _inJoinCondition = false;
    }

    // GROUP BY
    public override void Visit(GroupByClause node)
    {
        if (_cteDepth > 0) return; // Skip GROUP BY inside CTEs
        _inGroupBy = true;
        base.Visit(node);
        _inGroupBy = false;
    }

    public override void Visit(ExpressionGroupingSpecification node)
    {
        if (!_inGroupBy || _cteDepth > 0) { base.Visit(node); return; }
        if (node.Expression is ColumnReferenceExpression colRef)
        {
            GroupByColumns.Add(ColumnReferenceFactory.Create(colRef, alias: null, ColumnUsageType.GroupBy));
        }
        else
        {
            var innerVisitor = new GroupByExpressionColumnVisitor();
            node.Expression.Accept(innerVisitor);
            GroupByColumns.AddRange(innerVisitor.Columns);
        }
    }

    // ORDER BY
    public override void Visit(ExpressionWithSortOrder node)
    {
        var isAscending = node.SortOrder != SortOrder.Descending;
        if (node.Expression is ColumnReferenceExpression colRef)
        {
            var column = ColumnReferenceFactory.Create(colRef, alias: null, ColumnUsageType.OrderBy, isAscending);
            OrderByColumns.Add(column);
        }
        else
        {
            var innerVisitor = new OrderByExpressionColumnVisitor(isAscending);
            node.Expression.Accept(innerVisitor);
            OrderByColumns.AddRange(innerVisitor.Columns);
        }
    }

    // Nested helper visitors
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
}
