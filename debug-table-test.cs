using SqlQueryAnalyzer;
using SqlQueryAnalyzer.Models;

var analyzer = new SqlQueryAnalyzerService();

var sql = """
    SELECT 
        p.id,
        (SELECT COUNT(*) FROM Orders o WHERE o.customer_id = p.id) as order_count
    FROM Products p
    """;

var result = analyzer.Analyze(sql, new AnalysisOptions { IncludeInnerTables = true });

Console.WriteLine("=== ALL TABLES (IncludeInnerTables = true) ===");
foreach (var table in result.Tables)
{
    Console.WriteLine($"  {table.TableName} AS {table.Alias} - DirectReference: {table.DirectReference}");
}

var result2 = analyzer.Analyze(sql, new AnalysisOptions { IncludeInnerTables = false });

Console.WriteLine("\n=== FILTERED TABLES (IncludeInnerTables = false) ===");
foreach (var table in result2.Tables)
{
    Console.WriteLine($"  {table.TableName} AS {table.Alias} - DirectReference: {table.DirectReference}");
}