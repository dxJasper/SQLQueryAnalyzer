using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DXMS.SqlQueryAnalyzer.Visitors;

/// <summary>
/// Factory for creating <see cref="ColumnReference"/> instances from TSql AST nodes.
/// Centralizes the column extraction logic to reduce duplication across visitors.
/// </summary>
internal static class ColumnReferenceFactory
{
    /// <summary>
    /// Creates a <see cref="ColumnReference"/> from a <see cref="ColumnReferenceExpression"/>.
    /// </summary>
    /// <param name="node">The column reference expression from the AST.</param>
    /// <param name="alias">Optional alias for the column.</param>
    /// <param name="usageType">The context in which the column is used.</param>
    /// <param name="isAscending">Sort order for ORDER BY usage (defaults to true).</param>
    /// <returns>A new <see cref="ColumnReference"/> instance.</returns>
    public static ColumnReference Create(ColumnReferenceExpression node, string? alias, ColumnUsageType usageType, bool isAscending = true)
    {
        var identifiers = node.MultiPartIdentifier?.Identifiers;

        return identifiers?.Count switch
        {
            1 => new ColumnReference
            {
                ColumnName = identifiers[0].Value,
                Alias = alias,
                UsageType = usageType,
                IsAscending = isAscending,
                Kind = ColumnKind.Column,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            },
            2 => new ColumnReference
            {
                TableAlias = identifiers[0].Value,
                ColumnName = identifiers[1].Value,
                Alias = alias,
                UsageType = usageType,
                IsAscending = isAscending,
                Kind = ColumnKind.Column,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            },
            3 => new ColumnReference
            {
                Schema = identifiers[0].Value,
                TableName = identifiers[1].Value,
                ColumnName = identifiers[2].Value,
                Alias = alias,
                UsageType = usageType,
                IsAscending = isAscending,
                Kind = ColumnKind.Column,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            },
            4 => new ColumnReference
            {
                // 4-part: Database.Schema.Table.Column - skip database
                Schema = identifiers[1].Value,
                TableName = identifiers[2].Value,
                ColumnName = identifiers[3].Value,
                Alias = alias,
                UsageType = usageType,
                IsAscending = isAscending,
                Kind = ColumnKind.Column,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            },
            _ => new ColumnReference
            {
                ColumnName = "[Unknown]",
                Alias = alias,
                UsageType = usageType,
                IsAscending = isAscending,
                Kind = ColumnKind.Column,
                StartLine = node.StartLine,
                StartColumn = node.StartColumn
            }
        };
    }

    /// <summary>
    /// Creates a <see cref="ColumnReference"/> for a star (*) expression.
    /// </summary>
    /// <param name="node">The star expression node.</param>
    /// <returns>A new <see cref="ColumnReference"/> representing the star.</returns>
    public static ColumnReference CreateStar(SelectStarExpression node)
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

    /// <summary>
    /// Creates a <see cref="ColumnReference"/> for a computed expression.
    /// </summary>
    /// <param name="node">The scalar expression.</param>
    /// <param name="alias">Optional alias for the expression.</param>
    /// <param name="usageType">The context in which the expression is used.</param>
    /// <param name="baseColumn">Optional base column reference extracted from the expression.</param>
    /// <param name="isAscending">Sort order for ORDER BY usage (defaults to true).</param>
    /// <returns>A new <see cref="ColumnReference"/> representing the expression.</returns>
    public static ColumnReference CreateExpression(ScalarExpression node, string? alias, ColumnUsageType usageType, ColumnReference? baseColumn = null, bool isAscending = true)
    {
        var expressionText = SqlQueryAnalyzerService.GetFragmentText(node);

        return new ColumnReference
        {
            TableAlias = baseColumn?.TableAlias,
            TableName = baseColumn?.TableName,
            Schema = baseColumn?.Schema,
            ColumnName = baseColumn?.ColumnName ?? "[Expression]",
            Alias = alias,
            Expression = expressionText,
            UsageType = usageType,
            IsAscending = isAscending,
            Kind = FunctionUtils.DetermineKind(node),
            StartLine = node.StartLine,
            StartColumn = node.StartColumn
        };
    }
}
