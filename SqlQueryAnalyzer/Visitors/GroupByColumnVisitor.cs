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
    
    public override void Visit(GroupByClause node)
    {
        _inGroupByClause = true;
        base.Visit(node);
        _inGroupByClause = false;
    }
    
    public override void Visit(ExpressionGroupingSpecification node)
    {
        if (!_inGroupByClause)
        {
            base.Visit(node);
            return;
        }
        
        // Direct column reference
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
            var innerVisitor = new ExpressionColumnVisitor(ColumnUsageType.GroupBy);
            node.Expression.Accept(innerVisitor);
            Columns.AddRange(innerVisitor.Columns);
        }
    }
    
    public override void Visit(ColumnReferenceExpression node)
    {
        // Only process if we're directly in a GROUP BY context and not already handled
        if (!_inGroupByClause)
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
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            2 => new ColumnReference
            {
                TableAlias = identifiers[0].Value,
                ColumnName = identifiers[1].Value,
                UsageType = ColumnUsageType.GroupBy,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            3 => new ColumnReference
            {
                Schema = identifiers[0].Value,
                TableName = identifiers[1].Value,
                ColumnName = identifiers[2].Value,
                UsageType = ColumnUsageType.GroupBy,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            4 => new ColumnReference
            {
                Schema = identifiers[1].Value,
                TableName = identifiers[2].Value,
                ColumnName = identifiers[3].Value,
                UsageType = ColumnUsageType.GroupBy,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            _ => null
        };
    }
}
