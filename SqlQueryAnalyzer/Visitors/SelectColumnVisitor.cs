using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlQueryAnalyzer.Models;

namespace SqlQueryAnalyzer.Visitors;

/// <summary>
/// Visits and extracts all columns from SELECT clauses
/// </summary>
internal sealed class SelectColumnVisitor : TSqlConcreteFragmentVisitor
{
    public List<ColumnReference> Columns { get; } = [];
    
    public override void Visit(SelectScalarExpression node)
    {
        var alias = node.ColumnName?.Value;
        
        // Handle direct column reference
        if (node.Expression is ColumnReferenceExpression colRef)
        {
            var column = ExtractColumnReference(colRef, alias, ColumnUsageType.Select);
            Columns.Add(column);
        }
        // Handle expression (CASE, function, etc.)
        else
        {
            var expressionText = SqlQueryAnalyzerService.GetFragmentText(node.Expression);
            
            // Extract columns used within the expression
            var innerVisitor = new ExpressionColumnVisitor(ColumnUsageType.Select);
            node.Expression.Accept(innerVisitor);
            
            if (innerVisitor.Columns.Count > 0)
            {
                // Add the first column with the alias and expression
                var firstCol = innerVisitor.Columns[0];
                Columns.Add(new ColumnReference
                {
                    TableAlias = firstCol.TableAlias,
                    TableName = firstCol.TableName,
                    Schema = firstCol.Schema,
                    ColumnName = firstCol.ColumnName,
                    Alias = alias,
                    Expression = expressionText,
                    UsageType = ColumnUsageType.Select,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                });
                
                // Add remaining columns without alias
                foreach (var col in innerVisitor.Columns.Skip(1))
                {
                    Columns.Add(col);
                }
            }
            else if (alias is not null)
            {
                // Expression without column references (e.g., 'Prospect' AS CATCLI)
                Columns.Add(new ColumnReference
                {
                    ColumnName = "[Expression]",
                    Alias = alias,
                    Expression = expressionText,
                    UsageType = ColumnUsageType.Select,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                });
            }
        }
        
        // Don't call base to avoid double-visiting columns
    }
    
    public override void Visit(SelectStarExpression node)
    {
        var column = new ColumnReference
        {
            TableAlias = node.Qualifier?.Identifiers.LastOrDefault()?.Value,
            ColumnName = "*",
            UsageType = ColumnUsageType.Select,
            StartLine = node.StartLine,
            StartColumn = node.StartColumn
        };
        
        Columns.Add(column);
    }
    
    internal static ColumnReference ExtractColumnReference(
        ColumnReferenceExpression colRef, 
        string? alias, 
        ColumnUsageType usageType)
    {
        var identifiers = colRef.MultiPartIdentifier?.Identifiers;
        
        return identifiers?.Count switch
        {
            1 => new ColumnReference
            {
                ColumnName = identifiers[0].Value,
                Alias = alias,
                UsageType = usageType,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            2 => new ColumnReference
            {
                TableAlias = identifiers[0].Value,
                ColumnName = identifiers[1].Value,
                Alias = alias,
                UsageType = usageType,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            3 => new ColumnReference
            {
                Schema = identifiers[0].Value,
                TableName = identifiers[1].Value,
                ColumnName = identifiers[2].Value,
                Alias = alias,
                UsageType = usageType,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            4 => new ColumnReference
            {
                Schema = identifiers[1].Value,
                TableName = identifiers[2].Value,
                ColumnName = identifiers[3].Value,
                Alias = alias,
                UsageType = usageType,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            _ => new ColumnReference
            {
                ColumnName = "[Unknown]",
                Alias = alias,
                UsageType = usageType,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            }
        };
    }
}

/// <summary>
/// Helper visitor to extract column references from expressions
/// </summary>
internal sealed class ExpressionColumnVisitor : TSqlConcreteFragmentVisitor
{
    private readonly ColumnUsageType _usageType;
    
    public List<ColumnReference> Columns { get; } = [];
    
    public ExpressionColumnVisitor(ColumnUsageType usageType)
    {
        _usageType = usageType;
    }
    
    public override void Visit(ColumnReferenceExpression node)
    {
        var identifiers = node.MultiPartIdentifier?.Identifiers;
        
        var column = identifiers?.Count switch
        {
            1 => new ColumnReference
            {
                ColumnName = identifiers[0].Value,
                UsageType = _usageType,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            },
            2 => new ColumnReference
            {
                TableAlias = identifiers[0].Value,
                ColumnName = identifiers[1].Value,
                UsageType = _usageType,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            },
            3 => new ColumnReference
            {
                Schema = identifiers[0].Value,
                TableName = identifiers[1].Value,
                ColumnName = identifiers[2].Value,
                UsageType = _usageType,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            },
            _ => new ColumnReference
            {
                ColumnName = "[Unknown]",
                UsageType = _usageType,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            }
        };
        
        Columns.Add(column);
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
