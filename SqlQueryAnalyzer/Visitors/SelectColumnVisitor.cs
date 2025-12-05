using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlQueryAnalyzer.Models;

namespace SqlQueryAnalyzer.Visitors;

/// <summary>
/// Visits and extracts all columns from SELECT clauses
/// </summary>
internal sealed class SelectColumnVisitor : TSqlConcreteFragmentVisitor
{
    public List<ColumnReference> Columns { get; } = [];

    private int _cteDepth;
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
        // Skip inner query traversal
        _subqueryDepth--;
    }

    public override void Visit(ScalarSubquery node)
    {
        _subqueryDepth++;
        // Skip inner query traversal
        _subqueryDepth--;
    }

    public override void Visit(SelectScalarExpression node)
    {
        if (_cteDepth > 0 || _subqueryDepth > 0)
        {
            return;
        }

        var alias = node.ColumnName?.Value;

        if (node.Expression is ColumnReferenceExpression colRef)
        {
            var column = ExtractColumnReference(colRef, alias, ColumnUsageType.Select);
            Columns.Add(column);
            return;
        }

        var expressionText = SqlQueryAnalyzerService.GetFragmentText(node.Expression);
        string baseColumnName = "[Expression]";
        string? tableAlias = null;
        string? tableName = null;
        string? schema = null;

        var innerVisitor = new ExpressionColumnVisitor(ColumnUsageType.Select);
        node.Expression.Accept(innerVisitor);
        if (innerVisitor.Columns.Count > 0)
        {
            var first = innerVisitor.Columns[0];
            baseColumnName = first.ColumnName;
            tableAlias = first.TableAlias;
            tableName = first.TableName;
            schema = first.Schema;
        }

        Columns.Add(new ColumnReference
        {
            TableAlias = tableAlias,
            TableName = tableName,
            Schema = schema,
            ColumnName = baseColumnName,
            Alias = alias,
            Expression = expressionText,
            UsageType = ColumnUsageType.Select,
            Kind = DetermineKind(node.Expression),
            StartLine = node.StartLine,
            StartColumn = node.StartColumn
        });
    }

    public override void Visit(SelectStarExpression node)
    {
        if (_cteDepth > 0 || _subqueryDepth > 0)
        {
            return;
        }
        var column = new ColumnReference
        {
            TableAlias = node.Qualifier?.Identifiers.LastOrDefault()?.Value,
            ColumnName = "*",
            UsageType = ColumnUsageType.Select,
            Kind = ColumnKind.Star,
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
                Kind = ColumnKind.Column,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            2 => new ColumnReference
            {
                TableAlias = identifiers[0].Value,
                ColumnName = identifiers[1].Value,
                Alias = alias,
                UsageType = usageType,
                Kind = ColumnKind.Column,
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
                Kind = ColumnKind.Column,
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
                Kind = ColumnKind.Column,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            },
            _ => new ColumnReference
            {
                ColumnName = "[Unknown]",
                Alias = alias,
                UsageType = usageType,
                Kind = ColumnKind.Column,
                StartLine = colRef.StartLine,
                StartColumn = colRef.StartColumn
            }
        };
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
                    Kind = ColumnKind.Column,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                },
                2 => new ColumnReference
                {
                    TableAlias = identifiers[0].Value,
                    ColumnName = identifiers[1].Value,
                    UsageType = _usageType,
                    Kind = ColumnKind.Column,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                },
                3 => new ColumnReference
                {
                    Schema = identifiers[0].Value,
                    TableName = identifiers[1].Value,
                    ColumnName = identifiers[2].Value,
                    UsageType = _usageType,
                    Kind = ColumnKind.Column,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                },
                _ => new ColumnReference
                {
                    ColumnName = "[Unknown]",
                    UsageType = _usageType,
                    Kind = ColumnKind.Column,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                }
            };
            
            Columns.Add(column);
            base.Visit(node);
        }
    }
    
    private static ColumnKind DetermineKind(ScalarExpression expr) => expr switch
    {
        FunctionCall func when IsAggregate(func) => ColumnKind.Aggregate,
        FunctionCall => ColumnKind.Function,
        CaseExpression => ColumnKind.Expression,
        BinaryExpression => ColumnKind.Expression,
        UnaryExpression => ColumnKind.Expression,
        StringLiteral => ColumnKind.Literal,
        IntegerLiteral => ColumnKind.Literal,
        NumericLiteral => ColumnKind.Literal,
        NullLiteral => ColumnKind.Literal,
        ScalarSubquery => ColumnKind.Subquery,
        _ => ColumnKind.Expression
    };
    
    private static bool IsAggregate(FunctionCall func)
    {
        var name = func.FunctionName?.Value?.ToUpperInvariant();
        return name is "SUM" or "COUNT" or "AVG" or "MIN" or "MAX" or "STDEV" or "STDEVP" or "VAR" or "VARP" or "COUNT_BIG" or "GROUPING" or "GROUPING_ID" or "STRING_AGG" or "APPROX_COUNT_DISTINCT";
    }
}
