namespace SqlQueryAnalyzer.Models;

/// <summary>
/// Complete analysis result of a SQL query
/// </summary>
public sealed class QueryAnalysisResult
{
    public required string OriginalQuery { get; init; }
    public List<string> ParseErrors { get; init; } = [];
    public List<QueryTableReference> Tables { get; init; } = [];
    public List<ColumnReference> SelectColumns { get; init; } = [];
    public List<ColumnReference> PredicateColumns { get; init; } = [];
    public List<ColumnReference> JoinColumns { get; init; } = [];
    public List<ColumnReference> GroupByColumns { get; init; } = [];
    public List<ColumnReference> OrderByColumns { get; init; } = [];
    public List<CteDefinition> CommonTableExpressions { get; init; } = [];
    public List<SubQueryInfo> SubQueries { get; init; } = [];
    public List<ColumnLineage> ColumnLineages { get; init; } = [];

    public bool HasErrors => ParseErrors.Count > 0;

    /// <summary>
    /// All unique schemas referenced in the query
    /// </summary>
    public IEnumerable<string> Schemas => Tables
        .Where(t => !string.IsNullOrEmpty(t.Schema))
        .Select(t => t.Schema!)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// All columns grouped by their usage type
    /// </summary>
    public ILookup<ColumnUsageType, ColumnReference> ColumnsByUsage =>
        SelectColumns
            .Concat(PredicateColumns)
            .Concat(JoinColumns)
            .Concat(GroupByColumns)
            .Concat(OrderByColumns)
            .ToLookup(c => c.UsageType);
}

/// <summary>
/// Represents a table reference in the query
/// </summary>
public sealed class QueryTableReference
{
    public string? Database { get; init; }
    public string? Schema { get; init; }
    public required string TableName { get; init; }
    public string? Alias { get; init; }
    public TableReferenceType Type { get; init; }
    public JoinType? JoinType { get; init; }
    public int StartLine { get; init; }
    public int StartColumn { get; init; }

    public string FullName => string.Join(".",
        new[] { Database, Schema, TableName }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>
    /// The identifier used to reference this table (alias if available, otherwise table name)
    /// </summary>
    public string ReferenceIdentifier => Alias ?? TableName;

    public override string ToString() =>
        Alias is not null ? $"{FullName} AS {Alias}" : FullName;
}

public enum TableReferenceType
{
    Table,
    View,
    Cte,
    DerivedTable,
    TableValuedFunction
}

public enum JoinType
{
    Inner,
    Left,
    Right,
    Full,
    Cross
}

/// <summary>
/// Represents a column reference in the query
/// </summary>
public sealed class ColumnReference
{
    public string? TableAlias { get; init; }
    public string? TableName { get; init; }
    public string? Schema { get; init; }
    public required string ColumnName { get; init; }
    public string? Alias { get; init; }
    public ColumnUsageType UsageType { get; init; }
    public string? Expression { get; init; }
    public bool IsAscending { get; init; } = true;
    public int StartLine { get; init; }
    public int StartColumn { get; init; }

    /// <summary>
    /// The table/alias this column is associated with
    /// </summary>
    public string? SourceIdentifier => TableAlias ?? TableName;

    public string FullName => string.Join(".",
        new[] { Schema, TableAlias ?? TableName, ColumnName }.Where(s => !string.IsNullOrEmpty(s)));

    public override string ToString() =>
        Alias is not null ? $"{FullName} AS {Alias}" : FullName;
}

public enum ColumnUsageType
{
    Select,
    Where,
    Join,
    GroupBy,
    OrderBy,
    Having,
    CaseWhen,
    Function,
    Subquery
}

/// <summary>
/// Represents column lineage - tracking where output columns come from
/// </summary>
public sealed class ColumnLineage
{
    /// <summary>
    /// The output column name (alias or original name)
    /// </summary>
    public required string OutputColumn { get; init; }

    /// <summary>
    /// The output column alias (if any)
    /// </summary>
    public string? OutputAlias { get; init; }

    /// <summary>
    /// Source columns that contribute to this output
    /// </summary>
    public List<SourceColumn> SourceColumns { get; init; } = [];

    /// <summary>
    /// Whether this is a computed/derived column
    /// </summary>
    public bool IsComputed { get; init; }

    /// <summary>
    /// The expression used to compute the column (if applicable)
    /// </summary>
    public string? Expression { get; init; }

    /// <summary>
    /// Type of transformation applied
    /// </summary>
    public TransformationType Transformation { get; init; }
}

/// <summary>
/// Represents a source column in lineage tracking
/// </summary>
public sealed class SourceColumn
{
    public string? Database { get; init; }
    public string? Schema { get; init; }
    public string? TableName { get; init; }
    public string? TableAlias { get; init; }
    public required string ColumnName { get; init; }

    public string FullyQualifiedName => string.Join(".",
        new[] { Database, Schema, TableName, ColumnName }.Where(s => !string.IsNullOrEmpty(s)));

    public override string ToString() => FullyQualifiedName;
}

public enum TransformationType
{
    Direct,           // Direct column reference
    Alias,            // Simple alias
    Cast,             // CAST/CONVERT
    Function,         // Function call (e.g., UPPER, COALESCE)
    Aggregate,        // Aggregate function (SUM, COUNT, etc.)
    Case,             // CASE expression
    Arithmetic,       // Mathematical operation
    Concatenation,    // String concatenation
    Literal,          // Literal value
    Subquery,         // Scalar subquery
    WindowFunction,   // Window/analytic function
    Unknown
}

/// <summary>
/// Represents a CTE definition
/// </summary>
public sealed class CteDefinition
{
    public required string Name { get; init; }
    public List<string> ColumnList { get; init; } = [];
    public required string QueryText { get; init; }
    public QueryAnalysisResult? InnerAnalysis { get; init; }
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
}

/// <summary>
/// Represents a subquery
/// </summary>
public sealed class SubQueryInfo
{
    public string? Alias { get; init; }
    public required string QueryText { get; init; }
    public SubQueryType Type { get; init; }
    public QueryAnalysisResult? InnerAnalysis { get; init; }
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
}

public enum SubQueryType
{
    DerivedTable,
    ScalarSubquery,
    ExistsSubquery,
    InSubquery
}
