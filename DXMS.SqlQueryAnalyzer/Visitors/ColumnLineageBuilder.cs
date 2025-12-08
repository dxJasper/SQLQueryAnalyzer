using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DXMS.SqlQueryAnalyzer.Visitors;

/// <summary>
/// Builds column lineage information - tracking where output columns come from
/// </summary>
internal sealed class ColumnLineageBuilder : TSqlConcreteFragmentVisitor
{
    private readonly List<QueryTableReference> _tables;
    private readonly Dictionary<string, QueryTableReference> _tableByAlias;
    private int _cteDepth;

    public List<ColumnLineage> Lineages { get; } = [];

    public ColumnLineageBuilder(List<QueryTableReference> tables)
    {
        _tables = tables;
        _tableByAlias = tables
            .Where(tableReference => !string.IsNullOrEmpty(tableReference.Alias))
            .GroupBy(tableReference => tableReference.Alias!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

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

    public override void Visit(SelectScalarExpression node)
    {
        if (_cteDepth > 0)
        {
            return; // Skip if inside CTE
        }

        var outputAlias = node.ColumnName?.Value;
        var lineage = new ColumnLineage
        {
            OutputColumn = outputAlias ?? GetExpressionName(node.Expression),
            OutputAlias = outputAlias,
            Expression = SqlQueryAnalyzerService.GetFragmentText(node.Expression),
            Transformation = DetermineTransformationType(node.Expression),
            IsComputed = node.Expression is not ColumnReferenceExpression
        };

        // Extract source columns
        var sourceExtractor = new SourceColumnExtractor(_tables, _tableByAlias);
        node.Expression.Accept(sourceExtractor);
        lineage.SourceColumns.AddRange(sourceExtractor.SourceColumns);

        Lineages.Add(lineage);
    }

    public override void Visit(SelectStarExpression node)
    {
        if (_cteDepth > 0)
        {
            return; // Skip if inside CTE
        }

        var tableAlias = node.Qualifier?.Identifiers.LastOrDefault()?.Value;

        if (tableAlias is not null && _tableByAlias.TryGetValue(tableAlias, out var table))
        {
            // Star from specific table
            Lineages.Add(new ColumnLineage
            {
                OutputColumn = $"{tableAlias}.*",
                Transformation = TransformationType.Direct,
                IsComputed = false,
                SourceColumns =
                [
                    new SourceColumn
                    {
                        Schema = table.Schema,
                        TableName = table.TableName,
                        TableAlias = table.Alias,
                        ColumnName = "*"
                    }
                ]
            });
        }
        else
        {
            // Star from all tables
            Lineages.Add(new ColumnLineage
            {
                OutputColumn = "*",
                Transformation = TransformationType.Direct,
                IsComputed = false,
                SourceColumns = _tables.Select(reference => new SourceColumn
                {
                    Schema = reference.Schema,
                    TableName = reference.TableName,
                    TableAlias = reference.Alias,
                    ColumnName = "*"
                }).ToList()
            });
        }
    }

    private static string GetExpressionName(ScalarExpression expression) => expression switch
    {
        ColumnReferenceExpression col => col.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value ?? "[Column]",
        FunctionCall func => func.FunctionName?.Value ?? "[Function]",
        CastCall => "[Cast]",
        ConvertCall => "[Convert]",
        CaseExpression => "[Case]",
        CoalesceExpression => "[Coalesce]",
        NullIfExpression => "[NullIf]",
        IIfCall => "[IIf]",
        ScalarSubquery => "[Subquery]",
        _ => "[Expression]"
    };

    private static TransformationType DetermineTransformationType(ScalarExpression expression) => expression switch
    {
        ColumnReferenceExpression => TransformationType.Direct,
        CastCall => TransformationType.Cast,
        ConvertCall => TransformationType.Cast,
        FunctionCall func => FunctionUtils.IsAggregateName(func.FunctionName?.Value)
            ? TransformationType.Aggregate
            : TransformationType.Function,
        CaseExpression => TransformationType.Case,
        CoalesceExpression => TransformationType.Function,
        NullIfExpression => TransformationType.Function,
        IIfCall => TransformationType.Case,
        BinaryExpression => TransformationType.Arithmetic,
        UnaryExpression => TransformationType.Arithmetic,
        StringLiteral => TransformationType.Literal,
        IntegerLiteral => TransformationType.Literal,
        NumericLiteral => TransformationType.Literal,
        NullLiteral => TransformationType.Literal,
        ScalarSubquery => TransformationType.Subquery,
        ParenthesisExpression paren => DetermineTransformationType(paren.Expression),
        _ => TransformationType.Unknown
    };

    private static bool IsAggregateFunction(string? functionName) =>
        FunctionUtils.IsAggregateName(functionName);

    public override void Visit(ScalarSubquery node)
    {
        // Don't traverse into scalar subqueries
    }

    public override void Visit(QueryDerivedTable node)
    {
        // Don't traverse into derived table subqueries
    }
}

/// <summary>
/// Extracts source columns from an expression
/// </summary>
internal sealed class SourceColumnExtractor(
    List<QueryTableReference> tables,
    Dictionary<string, QueryTableReference> tableByAlias)
    : TSqlConcreteFragmentVisitor
{
    public List<SourceColumn> SourceColumns { get; } = [];

    public override void Visit(ColumnReferenceExpression node)
    {
        var identifiers = node.MultiPartIdentifier?.Identifiers;
        if (identifiers is null or { Count: 0 })
        {
            base.Visit(node);
            return;
        }

        var sourceColumn = identifiers.Count switch
        {
            1 => CreateSourceColumnWithTableResolution(null, identifiers[0].Value),
            2 => CreateSourceColumnWithTableResolution(identifiers[0].Value, identifiers[1].Value),
            3 => new SourceColumn
            {
                Schema = identifiers[0].Value,
                TableName = identifiers[1].Value,
                ColumnName = identifiers[2].Value
            },
            4 => new SourceColumn
            {
                Database = identifiers[0].Value,
                Schema = identifiers[1].Value,
                TableName = identifiers[2].Value,
                ColumnName = identifiers[3].Value
            },
            _ => new SourceColumn { ColumnName = identifiers.Last().Value }
        };

        SourceColumns.Add(sourceColumn);
        base.Visit(node);
    }

    private SourceColumn CreateSourceColumnWithTableResolution(string? tableAliasOrName, string columnName)
    {
        if (tableAliasOrName is not null)
        {
            // Try to resolve the alias to a table
            if (tableByAlias.TryGetValue(tableAliasOrName, out var table))
            {
                return new SourceColumn
                {
                    Schema = table.Schema,
                    TableName = table.TableName,
                    TableAlias = table.Alias,
                    ColumnName = columnName
                };
            }

            // Check if it's a direct table name match
            var matchingTable = tables.FirstOrDefault(t =>
                string.Equals(t.TableName, tableAliasOrName, StringComparison.OrdinalIgnoreCase));

            if (matchingTable is not null)
            {
                return new SourceColumn
                {
                    Schema = matchingTable.Schema,
                    TableName = matchingTable.TableName,
                    TableAlias = matchingTable.Alias,
                    ColumnName = columnName
                };
            }

            // Could not resolve - return as-is
            return new SourceColumn
            {
                TableAlias = tableAliasOrName,
                ColumnName = columnName
            };
        }

        // No table qualifier - column could be from any table
        // In a real scenario, you'd need schema information to resolve this
        return new SourceColumn
        {
            ColumnName = columnName
        };
    }
}
