using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlQueryAnalyzer.Models;

namespace SqlQueryAnalyzer.Visitors;

/// <summary>
/// Extracts ONLY the final query SELECT columns using a targeted approach
/// </summary>
internal sealed class FinalQueryColumnVisitor : TSqlConcreteFragmentVisitor
{
    public List<ColumnReference> Columns { get; } = [];

    private QuerySpecification? _finalQuerySpec;
    private bool _foundFinalQuery = false;

    public override void Visit(TSqlScript node)
    {
        // For script-level queries, find the main statement
        base.Visit(node);
    }

    public override void Visit(SelectStatement node)
    {
        // This is a standalone SELECT statement - process it as the final query
        if (_foundFinalQuery)
        {
            return;
        }

        _foundFinalQuery = true;
        ProcessQueryExpression(node.QueryExpression);
    }

    public override void Visit(WithCtesAndXmlNamespaces node)
    {
        // For CTE queries, traverse normally but we'll skip CTE content via Visit(CommonTableExpression)
        if (_foundFinalQuery)
        {
            return;
        }

        _foundFinalQuery = true;
        base.Visit(node);
    }

    private void ProcessQueryExpression(QueryExpression? queryExpression)
    {
        while (true)
        {
            switch (queryExpression)
            {
                case QuerySpecification querySpec:
                    _finalQuerySpec = querySpec;
                    ProcessSelectElements(querySpec.SelectElements);
                    break;

                case BinaryQueryExpression binaryQuery:
                    // Handle UNION, INTERSECT, EXCEPT - process the final part
                    queryExpression = binaryQuery.SecondQueryExpression;
                    continue;
            }

            break;
        }
    }

    private void ProcessSelectElements(IList<SelectElement> selectElements)
    {
        foreach (var element in selectElements)
        {
            if (element is SelectScalarExpression scalarExpr)
            {
                var column = ProcessSelectScalarExpression(scalarExpr);
                if (column != null)
                {
                    Columns.Add(column);
                }
            }
            else if (element is SelectStarExpression starExpr)
            {
                var column = ProcessSelectStarExpression(starExpr);
                Columns.Add(column);
            }
        }
    }

    private ColumnReference? ProcessSelectScalarExpression(SelectScalarExpression node)
    {
        var alias = node.ColumnName?.Value;

        if (node.Expression is ColumnReferenceExpression colRef)
        {
            return ExtractColumnReference(colRef, alias, ColumnUsageType.Select);
        }

        var expressionText = SqlQueryAnalyzerService.GetFragmentText(node.Expression);
        var baseColumnName = "[Expression]";
        string? tableAlias = null;
        string? tableName = null;
        string? schema = null;

        // Extract base column info from the expression
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

        return new ColumnReference
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
        };
    }

    private static ColumnReference ProcessSelectStarExpression(SelectStarExpression node)
    {
        return new ColumnReference
        {
            TableAlias = node.Qualifier?.Identifiers.LastOrDefault()?.Value,
            ColumnName = "*",
            UsageType = ColumnUsageType.Select,
            Kind = ColumnKind.Star,
            StartLine = node.StartLine,
            StartColumn = node.StartColumn
        };
    }

    // Prevent any other visitation that might pick up CTE content
    public override void Visit(CommonTableExpression node)
    {
        // Completely ignore CTE definitions
    }

    public override void Visit(ScalarSubquery node)
    {
        // Ignore subqueries
    }

    public override void Visit(QueryDerivedTable node)
    {
        // Ignore derived tables
    }

    public static ColumnReference ExtractColumnReference(ColumnReferenceExpression colRef, string? alias, ColumnUsageType usageType)
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

    public sealed class ExpressionColumnVisitor(ColumnUsageType usageType) : TSqlConcreteFragmentVisitor
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
                    UsageType = usageType,
                    Kind = ColumnKind.Column,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                },
                2 => new ColumnReference
                {
                    TableAlias = identifiers[0].Value,
                    ColumnName = identifiers[1].Value,
                    UsageType = usageType,
                    Kind = ColumnKind.Column,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                },
                3 => new ColumnReference
                {
                    Schema = identifiers[0].Value,
                    TableName = identifiers[1].Value,
                    ColumnName = identifiers[2].Value,
                    UsageType = usageType,
                    Kind = ColumnKind.Column,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                },
                _ => new ColumnReference
                {
                    ColumnName = "[Unknown]",
                    UsageType = usageType,
                    Kind = ColumnKind.Column,
                    StartLine = node.StartLine,
                    StartColumn = node.StartColumn
                }
            };

            Columns.Add(column);
        }

        // Don't traverse into subqueries within expressions
        public override void Visit(ScalarSubquery node) { }
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