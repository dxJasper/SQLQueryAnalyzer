namespace DXMS.SqlQueryAnalyzer.Enums;

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