namespace SqlQueryAnalyzer;

/// <summary>
/// Configuration options for SQL query analysis.
/// Use <see cref="AnalysisOptionsBuilder"/> for fluent configuration.
/// </summary>
public sealed class AnalysisOptions
{
    /// <summary>
    /// When true, includes tables referenced in subqueries and derived tables.
    /// Default: true.
    /// </summary>
    public bool IncludeInnerTables { get; init; } = true;

    /// <summary>
    /// When true, removes duplicate entries from result collections.
    /// Default: true.
    /// </summary>
    public bool DeduplicateResults { get; init; } = true;

    /// <summary>
    /// When true, recursively analyzes CTEs and subqueries.
    /// Default: true.
    /// </summary>
    public bool AnalyzeNestedQueries { get; init; } = true;

    /// <summary>
    /// When true, builds column lineage information.
    /// Default: true.
    /// </summary>
    public bool BuildColumnLineage { get; init; } = true;

    /// <summary>
    /// Gets the default analysis options.
    /// </summary>
    public static AnalysisOptions Default { get; } = new();

    /// <summary>
    /// Creates an options builder for fluent configuration.
    /// </summary>
    /// <returns>A new <see cref="AnalysisOptionsBuilder"/> instance.</returns>
    public static AnalysisOptionsBuilder CreateBuilder() => new();
}

/// <summary>
/// Builder for creating <see cref="AnalysisOptions"/> with fluent syntax.
/// </summary>
public sealed class AnalysisOptionsBuilder
{
    private bool _includeInnerTables = true;
    private bool _deduplicateResults = true;
    private bool _analyzeNestedQueries = true;
    private bool _buildColumnLineage = true;

    /// <summary>
    /// Configures whether to include tables from subqueries and derived tables.
    /// </summary>
    /// <param name="include">True to include inner tables; false to exclude them.</param>
    /// <returns>The builder instance for chaining.</returns>
    public AnalysisOptionsBuilder IncludeInnerTables(bool include = true)
    {
        _includeInnerTables = include;
        return this;
    }

    /// <summary>
    /// Configures whether to deduplicate result collections.
    /// </summary>
    /// <param name="deduplicate">True to remove duplicates; false to keep all entries.</param>
    /// <returns>The builder instance for chaining.</returns>
    public AnalysisOptionsBuilder DeduplicateResults(bool deduplicate = true)
    {
        _deduplicateResults = deduplicate;
        return this;
    }

    /// <summary>
    /// Configures whether to analyze CTEs and subqueries recursively.
    /// </summary>
    /// <param name="analyze">True to analyze nested queries; false to skip them.</param>
    /// <returns>The builder instance for chaining.</returns>
    public AnalysisOptionsBuilder AnalyzeNestedQueries(bool analyze = true)
    {
        _analyzeNestedQueries = analyze;
        return this;
    }

    /// <summary>
    /// Configures whether to build column lineage information.
    /// </summary>
    /// <param name="build">True to build lineage; false to skip.</param>
    /// <returns>The builder instance for chaining.</returns>
    public AnalysisOptionsBuilder BuildColumnLineage(bool build = true)
    {
        _buildColumnLineage = build;
        return this;
    }

    /// <summary>
    /// Configures options for performance-optimized analysis (minimal processing).
    /// Disables nested query analysis and lineage building.
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public AnalysisOptionsBuilder ForPerformance()
    {
        _analyzeNestedQueries = false;
        _buildColumnLineage = false;
        return this;
    }

    /// <summary>
    /// Configures options for comprehensive analysis (all features enabled).
    /// </summary>
    /// <returns>The builder instance for chaining.</returns>
    public AnalysisOptionsBuilder ForComprehensive()
    {
        _includeInnerTables = true;
        _deduplicateResults = true;
        _analyzeNestedQueries = true;
        _buildColumnLineage = true;
        return this;
    }

    /// <summary>
    /// Builds the configured <see cref="AnalysisOptions"/> instance.
    /// </summary>
    /// <returns>A new <see cref="AnalysisOptions"/> with the configured values.</returns>
    public AnalysisOptions Build() => new()
    {
        IncludeInnerTables = _includeInnerTables,
        DeduplicateResults = _deduplicateResults,
        AnalyzeNestedQueries = _analyzeNestedQueries,
        BuildColumnLineage = _buildColumnLineage
    };
}
