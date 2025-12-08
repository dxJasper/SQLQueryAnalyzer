using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlQueryAnalyzer.Models;

namespace SqlQueryAnalyzer.Visitors;

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
        var identifiers = node.MultiPartIdentifier?.Identifiers;

        var column = identifiers?.Count switch
        {
            1 => new ColumnReference
            {
                ColumnName = identifiers[0].Value,
                UsageType = usageType,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            },
            2 => new ColumnReference
            {
                TableAlias = identifiers[0].Value,
                ColumnName = identifiers[1].Value,
                UsageType = usageType,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            },
            3 => new ColumnReference
            {
                Schema = identifiers[0].Value,
                TableName = identifiers[1].Value,
                ColumnName = identifiers[2].Value,
                UsageType = usageType,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            },
            4 => new ColumnReference
            {
                Schema = identifiers[1].Value,
                TableName = identifiers[2].Value,
                ColumnName = identifiers[3].Value,
                UsageType = usageType,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            },
            _ => null
        };

        if (column is not null)
        {
            Columns.Add(column);
        }

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
