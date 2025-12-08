# SqlQueryAnalyzer

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

## Requirements

- .NET 9.0 or later
- Microsoft.SqlServer.TransactSql.ScriptDom 170.128.0

## Installation

Add the project reference or install via NuGet (when published):

```bash
dotnet add reference SqlQueryAnalyzer.csproj
```

## Quick Start

```csharp
using SqlQueryAnalyzer;
using SqlQueryAnalyzer.Models;

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

## SQL Server Version Support

Supported SQL Server versions:

| Version | Enum Value | SQL Server Release |
|---------|------------|-------------------|
| SQL Server 2008 | `Sql100` | 10.0 |
| SQL Server 2012 | `Sql110` | 11.0 |
| SQL Server 2014 | `Sql120` | 12.0 |
| SQL Server 2016 | `Sql130` | 13.0 |
| SQL Server 2017 | `Sql140` | 14.0 |
| SQL Server 2019 | `Sql150` | 15.0 |
| SQL Server 2022 | `Sql160` | 16.0 (default) |

## Project Structure

```
SqlQueryAnalyzer/
├── SqlQueryAnalyzer/
│   ├── ISqlQueryAnalyzerService.cs    # Service interface
│   ├── SqlQueryAnalyzerService.cs     # Main analyzer implementation
│   ├── AnalysisOptions.cs             # Configuration options
│   ├── Models/
│   │   └── QueryAnalysisResult.cs     # Result models
│   ├── Visitors/
│   │   ├── ColumnReferenceFactory.cs  # Shared column extraction
│   │   ├── TableReferenceVisitor.cs   # Table extraction
│   │   ├── SelectColumnVisitor.cs     # SELECT column extraction
│   │   ├── PredicateColumnVisitor.cs  # WHERE column extraction
│   │   ├── JoinColumnVisitor.cs       # JOIN column extraction
│   │   ├── GroupByColumnVisitor.cs    # GROUP BY extraction
│   │   ├── OrderByColumnVisitor.cs    # ORDER BY extraction
│   │   ├── CteVisitor.cs              # CTE extraction
│   │   ├── SubQueryVisitor.cs         # Subquery extraction
│   │   ├── ColumnLineageBuilder.cs    # Lineage tracking
│   │   └── FinalQueryColumnVisitor.cs # Final output columns
│   └── Comparers/
│       └── EqualityComparers.cs       # Deduplication comparers
├── SqlQueryAnalyzer.Demo/             # Demo console application
└── SqlQueryAnalyzer.Tests/            # Unit tests (TUnit)
```

## Running Tests

```bash
cd SqlQueryAnalyzer.Tests
dotnet test
```

## License

MIT License - see LICENSE file for details.
