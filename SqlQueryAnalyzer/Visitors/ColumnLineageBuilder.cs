using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlQueryAnalyzer.Models;

namespace SqlQueryAnalyzer.Visitors;

/// <summary>
/// Builds column lineage information - tracking where output columns come from
/// </summary>
internal sealed class ColumnLineageBuilder : TSqlConcreteFragmentVisitor
{
    private readonly List<QueryTableReference> _tables;
    private readonly Dictionary<string, QueryTableReference> _tableByAlias;

    public List<ColumnLineage> Lineages { get; } = [];

    public ColumnLineageBuilder(List<QueryTableReference> tables)
    {
        _tables = tables;
        _tableByAlias = tables
            .Where(t => !string.IsNullOrEmpty(t.Alias))
            .GroupBy(t => t.Alias!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public override void Visit(SelectScalarExpression node)
    {
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
                SourceColumns = _tables.Select(t => new SourceColumn
                {
                    Schema = t.Schema,
                    TableName = t.TableName,
                    TableAlias = t.Alias,
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
        FunctionCall func => IsAggregateFunction(func.FunctionName?.Value)
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
        functionName?.ToUpperInvariant() switch
        {
            "SUM" or "COUNT" or "AVG" or "MIN" or "MAX" or
            "STDEV" or "STDEVP" or "VAR" or "VARP" or
            "COUNT_BIG" or "GROUPING" or "GROUPING_ID" or
            "STRING_AGG" or "APPROX_COUNT_DISTINCT" => true,
            _ => false
        };

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
