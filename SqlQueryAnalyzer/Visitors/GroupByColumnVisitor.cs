using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlQueryAnalyzer.Models;

namespace SqlQueryAnalyzer.Visitors;

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
            var column = ExtractColumnReference(colRef);
            if (column is not null)
            {
                Columns.Add(column);
            }
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

    private static ColumnReference? ExtractColumnReference(ColumnReferenceExpression colRef)
    {
        var identifiers = colRef.MultiPartIdentifier?.Identifiers;

        return identifiers?.Count switch
        {
            1 => new ColumnReference
            {
                ColumnName = identifiers[0].Value,
                UsageType = ColumnUsageType.GroupBy,
                Kind = ColumnKind.Column,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            2 => new ColumnReference
            {
                TableAlias = identifiers[0].Value,
                ColumnName = identifiers[1].Value,
                UsageType = ColumnUsageType.GroupBy,
                Kind = ColumnKind.Column,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            3 => new ColumnReference
            {
                Schema = identifiers[0].Value,
                TableName = identifiers[1].Value,
                ColumnName = identifiers[2].Value,
                UsageType = ColumnUsageType.GroupBy,
                Kind = ColumnKind.Column,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            4 => new ColumnReference
            {
                Schema = identifiers[1].Value,
                TableName = identifiers[2].Value,
                ColumnName = identifiers[3].Value,
                UsageType = ColumnUsageType.GroupBy,
                Kind = ColumnKind.Column,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            _ => null
        };
    }

    private sealed class GroupByExpressionColumnVisitor : TSqlConcreteFragmentVisitor
    {
        public List<ColumnReference> Columns { get; } = [];
        public override void Visit(ColumnReferenceExpression node)
        {
            var identifiers = node.MultiPartIdentifier?.Identifiers;
            var column = identifiers?.Count switch
            {
                1 => new ColumnReference
                {
                    ColumnName = identifiers[0].Value,
                    UsageType = ColumnUsageType.GroupBy,
                    Kind = ColumnKind.Column,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                },
                2 => new ColumnReference
                {
                    TableAlias = identifiers[0].Value,
                    ColumnName = identifiers[1].Value,
                    UsageType = ColumnUsageType.GroupBy,
                    Kind = ColumnKind.Column,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                },
                3 => new ColumnReference
                {
                    Schema = identifiers[0].Value,
                    TableName = identifiers[1].Value,
                    ColumnName = identifiers[2].Value,
                    UsageType = ColumnUsageType.GroupBy,
                    Kind = ColumnKind.Column,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                },
                _ => new ColumnReference
                {
                    ColumnName = "[Unknown]",
                    UsageType = ColumnUsageType.GroupBy,
                    Kind = ColumnKind.Column,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                }
            };
            Columns.Add(column);
            base.Visit(node);
        }
    }
}
