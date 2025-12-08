using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlQueryAnalyzer.Models;

namespace SqlQueryAnalyzer.Visitors;

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
            var column = ExtractColumnReference(colRef, isAscending);
            if (column is not null)
            {
                Columns.Add(column);
            }
        }
        else
        {
            var innerVisitor = new OrderByExpressionColumnVisitor();
            node.Expression.Accept(innerVisitor);

            foreach (var col in innerVisitor.Columns)
            {
                Columns.Add(new ColumnReference
                {
                    TableAlias = col.TableAlias,
                    TableName = col.TableName,
                    Schema = col.Schema,
                    ColumnName = col.ColumnName,
                    Alias = col.Alias,
                    Expression = SqlQueryAnalyzerService.GetFragmentText(node.Expression),
                    UsageType = ColumnUsageType.OrderBy,
                    IsAscending = isAscending,
                    Kind = ColumnKind.Column,
                    StartLine = col.StartLine,
                    StartColumn = col.StartColumn
                });
            }
        }
    }

    private static ColumnReference? ExtractColumnReference(ColumnReferenceExpression colRef, bool isAscending)
    {
        var identifiers = colRef.MultiPartIdentifier?.Identifiers;

        return identifiers?.Count switch
        {
            1 => new ColumnReference
            {
                ColumnName = identifiers[0].Value,
                UsageType = ColumnUsageType.OrderBy,
                IsAscending = isAscending,
                Kind = ColumnKind.Column,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            2 => new ColumnReference
            {
                TableAlias = identifiers[0].Value,
                ColumnName = identifiers[1].Value,
                UsageType = ColumnUsageType.OrderBy,
                IsAscending = isAscending,
                Kind = ColumnKind.Column,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            3 => new ColumnReference
            {
                Schema = identifiers[0].Value,
                TableName = identifiers[1].Value,
                ColumnName = identifiers[2].Value,
                UsageType = ColumnUsageType.OrderBy,
                IsAscending = isAscending,
                Kind = ColumnKind.Column,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            4 => new ColumnReference
            {
                Schema = identifiers[1].Value,
                TableName = identifiers[2].Value,
                ColumnName = identifiers[3].Value,
                UsageType = ColumnUsageType.OrderBy,
                IsAscending = isAscending,
                Kind = ColumnKind.Column,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            _ => null
        };
    }

    private sealed class OrderByExpressionColumnVisitor : TSqlConcreteFragmentVisitor
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
                    UsageType = ColumnUsageType.OrderBy,
                    Kind = ColumnKind.Column,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                },
                2 => new ColumnReference
                {
                    TableAlias = identifiers[0].Value,
                    ColumnName = identifiers[1].Value,
                    UsageType = ColumnUsageType.OrderBy,
                    Kind = ColumnKind.Column,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                },
                3 => new ColumnReference
                {
                    Schema = identifiers[0].Value,
                    TableName = identifiers[1].Value,
                    ColumnName = identifiers[2].Value,
                    UsageType = ColumnUsageType.OrderBy,
                    Kind = ColumnKind.Column,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                },
                _ => new ColumnReference
                {
                    ColumnName = "[Unknown]",
                    UsageType = ColumnUsageType.OrderBy,
                    Kind = ColumnKind.Column,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                }
            };
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
