using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DXMS.SqlQueryAnalyzer;

/// <summary>
/// Interface for SQL query analysis services.
/// Enables dependency injection and easier testing.
/// </summary>
public interface ISqlQueryAnalyzerService
{
    /// <summary>
    /// Analyzes a SQL query and returns detailed structural information.
    /// </summary>
    /// <param name="sql">The SQL query to analyze.</param>
    /// <returns>A <see cref="QueryAnalysisResult"/> containing the analysis results.</returns>
    QueryAnalysisResult Analyze(string sql);

    /// <summary>
    /// Analyzes multiple SQL statements (batch).
    /// </summary>
    /// <param name="sqlBatch">The SQL batch containing multiple statements.</param>
    /// <returns>An enumerable of analysis results, one per statement.</returns>
    IEnumerable<QueryAnalysisResult> AnalyzeBatch(string sqlBatch);

    /// <summary>
    /// Validates SQL syntax without performing full analysis.
    /// </summary>
    /// <param name="sql">The SQL query to validate.</param>
    /// <returns>A tuple indicating validity and any error messages.</returns>
    (bool IsValid, List<string> Errors) ValidateSyntax(string sql);

    /// <summary>
    /// Formats a SQL query according to the specified options.
    /// </summary>
    /// <param name="sql">The SQL query to format.</param>
    /// <param name="options">Optional formatting options.</param>
    /// <returns>The formatted SQL query.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the SQL is invalid.</exception>
    string Format(string sql, SqlScriptGeneratorOptions? options = null);
}
