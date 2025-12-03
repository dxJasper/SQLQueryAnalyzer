using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlQueryAnalyzer.Models;

namespace SqlQueryAnalyzer.Visitors;

/// <summary>
/// Visits and extracts all columns from JOIN ON clauses
/// </summary>
internal sealed class JoinColumnVisitor : TSqlConcreteFragmentVisitor
{
    public List<ColumnReference> Columns { get; } = [];
    
    private bool _inJoinCondition;
    
    public override void Visit(QualifiedJoin node)
    {
        // Visit the table references first
        node.FirstTableReference?.Accept(this);
        node.SecondTableReference?.Accept(this);
        
        // Now process the search condition (ON clause)
        if (node.SearchCondition is not null)
        {
            _inJoinCondition = true;
            node.SearchCondition.Accept(this);
            _inJoinCondition = false;
        }
    }
    
    public override void Visit(ColumnReferenceExpression node)
    {
        if (!_inJoinCondition)
        {
            base.Visit(node);
            return;
        }
        
        var identifiers = node.MultiPartIdentifier?.Identifiers;
        
        var column = identifiers?.Count switch
        {
            1 => new ColumnReference
            {
                ColumnName = identifiers[0].Value,
                UsageType = ColumnUsageType.Join,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            },
            2 => new ColumnReference
            {
                TableAlias = identifiers[0].Value,
                ColumnName = identifiers[1].Value,
                UsageType = ColumnUsageType.Join,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            },
            3 => new ColumnReference
            {
                Schema = identifiers[0].Value,
                TableName = identifiers[1].Value,
                ColumnName = identifiers[2].Value,
                UsageType = ColumnUsageType.Join,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            },
            4 => new ColumnReference
            {
                Schema = identifiers[1].Value,
                TableName = identifiers[2].Value,
                ColumnName = identifiers[3].Value,
                UsageType = ColumnUsageType.Join,
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

    // Prevent traversal into CTEs and subqueries - they're handled separately
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
