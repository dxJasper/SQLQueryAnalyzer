using SqlQueryAnalyzer;
using SqlQueryAnalyzer.Models;

var analyzer = new SqlQueryAnalyzerService();

var sql = """
    SELECT 
        c.category_name,
        p.supplier_id,
        COUNT(*) AS product_count
    FROM dbo.Products p
    INNER JOIN dbo.Categories c ON p.category_id = c.category_id
    GROUP BY c.category_name, p.supplier_id
    """;

var result = analyzer.Analyze(sql);

Console.WriteLine($"GroupByColumns count: {result.GroupByColumns.Count}");
foreach (var col in result.GroupByColumns)
{
    Console.WriteLine($"  • {col.TableAlias ?? col.TableName}.{col.ColumnName}");
}