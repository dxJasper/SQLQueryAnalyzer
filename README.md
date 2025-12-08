# DXMS.SqlQueryAnalyzer

A .NET library for analyzing T-SQL queries and extracting structural information including tables, columns, CTEs, subqueries, and column lineage.

## Features

- **Table Reference Extraction**: Identifies all tables, views, CTEs, and derived tables in a query
- **Column Analysis**: Extracts columns used in SELECT, WHERE, JOIN, GROUP BY, and ORDER BY clauses
- **CTE Support**: Parses and analyzes Common Table Expressions with recursive inner analysis
- **Subquery Detection**: Identifies derived tables, scalar subqueries, EXISTS, and IN subqueries
- **Column Lineage**: Tracks where output columns originate from (source tables/columns)
- **Final Query Columns**: Identifies the actual output columns the query returns
- **SQL Formatting**: Formats queries with configurable options
- **Batch Processing**: Analyzes multiple SQL statements in a single batch
- **Syntax Validation**: Validates SQL syntax without full analysis

## Visit pattern explained

- The parser turns SQL into an AST (TSqlFragment). Visitors are small classes inheriting TSqlConcreteFragmentVisitor that traverse this AST.

- Each visitor overrides Visit methods for node types it cares about (e.g., SelectScalarExpression, ColumnReferenceExpression) and collects data into lists (Tables, Columns, etc.).

- Traversal is initiated by calling fragment.Accept(visitor). The base visitor handles walking children; your overrides add logic and optionally skip parts.

- Separation of concerns: different visitors focus on specific domains (tables, select columns, joins, group by, subqueries, final query columns, lineage). The service orchestrates them in sequence.

- State tracking: visitors keep flags or counters (e.g., _inWhere, _inHaving, _subqueryDepth, _cteDepth) to control context-sensitive collection and avoid inner scopes like CTEs/subqueries.

- Extraction helpers: factories like ColumnReferenceFactory centralize building model objects; utilities like SqlQueryAnalyzerService.GetFragmentText get raw SQL for expressions.

- Result assembly: after all Accept calls, SqlQueryAnalyzerService merges visitor outputs into QueryAnalysisResult and deduplicates with custom comparers.

## Quick Start

```csharp
using DXMS.SqlQueryAnalyzer;
using DXMS.SqlQueryAnalyzer.Models;

// Create the analyzer (defaults to SQL Server 2022 syntax)
var analyzer = new SqlQueryAnalyzerService();

// Analyze a query
var sql = """
    SELECT 
        o.OrderId,
        c.CustomerName,
        SUM(oi.Quantity * oi.UnitPrice) AS TotalAmount
    FROM Orders o
    INNER JOIN Customers c ON o.CustomerId = c.CustomerId
    INNER JOIN OrderItems oi ON o.OrderId = oi.OrderId
    WHERE o.OrderDate >= '2024-01-01'
    GROUP BY o.OrderId, c.CustomerName
    ORDER BY TotalAmount DESC
    """;

var result = analyzer.Analyze(sql);

// Access the results
Console.WriteLine($"Tables: {string.Join(", ", result.Tables.Select(t => t.FullName))}");
Console.WriteLine($"Final columns: {string.Join(", ", result.FinalQueryColumns.Select(c => c.FullName))}");
```

## Analysis Options

Configure analysis behavior using the fluent builder:

```csharp
var options = AnalysisOptions.CreateBuilder()
    .IncludeInnerTables(true)      // Include tables from subqueries
    .DeduplicateResults(true)      // Remove duplicate entries
    .AnalyzeNestedQueries(true)    // Recursively analyze CTEs/subqueries
    .BuildColumnLineage(true)      // Track column origins
    .Build();

var result = analyzer.Analyze(sql, options);
```

Preset configurations:

```csharp
// Performance-optimized (skips nested analysis and lineage)
var fastOptions = AnalysisOptions.CreateBuilder()
    .ForPerformance()
    .Build();

// Comprehensive (all features enabled)
var fullOptions = AnalysisOptions.CreateBuilder()
    .ForComprehensive()
    .Build();
```

## Query Analysis Results

The `QueryAnalysisResult` provides access to:

| Property | Description |
|----------|-------------|
| `Tables` | All table references with schema, alias, and join type |
| `SelectColumns` | Columns in SELECT clauses |
| `PredicateColumns` | Columns in WHERE/HAVING clauses |
| `JoinColumns` | Columns used in JOIN conditions |
| `GroupByColumns` | Columns in GROUP BY clauses |
| `OrderByColumns` | Columns in ORDER BY clauses |
| `FinalQueryColumns` | The actual output columns of the query |
| `CommonTableExpressions` | CTE definitions with inner analysis |
| `SubQueries` | Subquery information with inner analysis |
| `ColumnLineages` | Source-to-output column mappings |
| `Schemas` | Unique schemas referenced |
| `ParseErrors` | Any syntax errors encountered |

## Column Lineage

Track where output columns come from:

```csharp
foreach (var lineage in result.ColumnLineages)
{
    Console.WriteLine($"Output: {lineage.OutputColumn}");
    foreach (var source in lineage.SourceColumns)
    {
        Console.WriteLine($"  ← {source.FullyQualifiedName}");
    }
}
```

## SQL Formatting

Format queries with customizable options:

```csharp
var formatted = analyzer.Format(sql, new SqlScriptGeneratorOptions
{
    KeywordCasing = KeywordCasing.Uppercase,
    IndentationSize = 4,
    NewLineBeforeFromClause = true,
    NewLineBeforeWhereClause = true
});
```

## Batch Processing

Analyze multiple statements:

```csharp
var batch = """
    SELECT * FROM Customers;
    SELECT * FROM Orders WHERE OrderDate > '2024-01-01';
    """;

foreach (var result in analyzer.AnalyzeBatch(batch))
{
    Console.WriteLine($"Query has {result.Tables.Count} tables");
}
```

## Dependency Injection

Register with your DI container using the interface:

```csharp
services.AddSingleton<ISqlQueryAnalyzerService>(
    new SqlQueryAnalyzerService(SqlServerVersion.Sql160));
```
