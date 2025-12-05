using SqlQueryAnalyzer;
using SqlQueryAnalyzer.Models;
using Xunit;
using System.Text;

namespace SqlQueryAnalyzer.Tests;

public class ComplexQueriesTests
{
    private readonly SqlQueryAnalyzerService _analyzer = new();
    private static readonly AnalysisOptions Options = new() { IncludeInnerTables = false, DeduplicateResults = true };

    [Fact]
    public void Analyze_ComplexJoinGroupOrder_Query_ReturnsExpectedStructures()
    {
        var sql = """
            SELECT 
                c.category_name,
                p.supplier_id,
                COUNT(*) AS product_count,
                SUM(p.unit_price * p.units_in_stock) AS total_value,
                AVG(p.unit_price) AS avg_price
            FROM dbo.Products p
            INNER JOIN dbo.Categories c ON p.category_id = c.category_id
            LEFT JOIN dbo.Suppliers s ON p.supplier_id = s.supplier_id
            WHERE p.discontinued = 0 AND p.unit_price > 10
            GROUP BY c.category_name, p.supplier_id
            HAVING COUNT(*) > 5
            ORDER BY total_value DESC, c.category_name ASC
        """;

        var result = _analyzer.Analyze(sql, Options);
        Assert.False(result.HasErrors);

        Assert.Contains(result.Tables, t => t.TableName == "Products" && t.Alias == "p");
        Assert.Contains(result.Tables, t => t.TableName == "Categories" && t.Alias == "c");
        Assert.Contains(result.Tables, t => t.TableName == "Suppliers" && t.Alias == "s");

        Assert.Contains(result.SelectColumns, c => c.ColumnName == "category_name" && c.TableAlias == "c");
        Assert.Contains(result.SelectColumns, c => c.ColumnName == "supplier_id" && c.TableAlias == "p");
        Assert.Contains(result.SelectColumns, c => c.Alias == "product_count");
        Assert.Contains(result.OrderByColumns, c => c.ColumnName == "total_value" && !c.IsAscending);

        Assert.Contains(result.ColumnLineages, l => (l.OutputAlias ?? l.OutputColumn) == "avg_price" && l.Transformation == TransformationType.Aggregate);
    }

    [Fact]
    public void Analyze_CteAndInnerQueries_ReturnsCteAndTopLevelTables()
    {
        var sql = """
            WITH ActiveCustomers AS (
                SELECT customer_id, name, email
                FROM dbo.Customers
                WHERE status = 'Active'
            ),
            RecentOrders AS (
                SELECT customer_id, COUNT(*) as order_count, MAX(order_date) as last_order
                FROM dbo.Orders
                WHERE order_date > DATEADD(month, -3, GETDATE())
                GROUP BY customer_id
            )
            SELECT 
                ac.customer_id,
                ac.name,
                ac.email,
                ro.order_count,
                ro.last_order
            FROM ActiveCustomers ac
            LEFT JOIN RecentOrders ro ON ac.customer_id = ro.customer_id
            WHERE ro.order_count > 5 OR ro.order_count IS NULL
            ORDER BY ro.order_count DESC
        """;

        var result = _analyzer.Analyze(sql, Options);
        Assert.False(result.HasErrors);

        Assert.Contains(result.Tables, t => t.Type == TableReferenceType.Cte && t.TableName == "ActiveCustomers");
        Assert.Contains(result.Tables, t => t.Type == TableReferenceType.Cte && t.TableName == "RecentOrders");

        Assert.Contains(result.SelectColumns, c => c.TableAlias == "ac" && c.ColumnName == "customer_id");
        Assert.Contains(result.SelectColumns, c => c.TableAlias == "ro" && c.ColumnName == "order_count");

        Assert.Contains(result.ColumnLineages, l => (l.OutputAlias ?? l.OutputColumn) == "last_order");
    }

    [Fact]
    public void Analyze_Subqueries_CollectsSubqueryInfoAndTables()
    {
        var sql = """
            SELECT 
                p.product_id,
                p.name,
                (SELECT AVG(price) FROM dbo.Products) as avg_price
            FROM dbo.Products p
            WHERE p.category_id IN (
                SELECT category_id 
                FROM dbo.Categories 
                WHERE active = 1
            )
            AND EXISTS (
                SELECT 1 
                FROM dbo.Inventory i 
                WHERE i.product_id = p.product_id AND i.quantity > 0
            )
        """;

        var result = _analyzer.Analyze(sql, Options);
        Assert.False(result.HasErrors);

        Assert.Contains(result.Tables, t => t.TableName == "Products" && t.Alias == "p");
        Assert.Contains(result.SubQueries, s => s.Type == SubQueryType.ScalarSubquery);
        Assert.Contains(result.SubQueries, s => s.Type == SubQueryType.InSubquery);
        Assert.Contains(result.SubQueries, s => s.Type == SubQueryType.ExistsSubquery);

        Assert.Contains(result.ColumnLineages, l => (l.OutputAlias ?? l.OutputColumn) == "avg_price" && l.Transformation == TransformationType.Subquery);
    }

    [Fact]
    public void Analyze_CaseExpressionsAndAggregates_LineageAndColumnsDetected()
    {
        var sql = """
            SELECT 
                CASE WHEN amount > 100 THEN 'High' ELSE 'Low' END AS amount_group,
                SUM(amount) AS total_amount,
                COUNT(*) AS cnt
            FROM dbo.Payments p
            WHERE p.status = 'OK'
            GROUP BY CASE WHEN amount > 100 THEN 'High' ELSE 'Low' END
        """;

        var result = _analyzer.Analyze(sql, Options);
        Assert.False(result.HasErrors);

        Assert.Contains(result.SelectColumns, c => c.Alias == "amount_group");
        Assert.Contains(result.SelectColumns, c => c.Alias == "total_amount");
        Assert.Contains(result.SelectColumns, c => c.Alias == "cnt");

        Assert.Contains(result.ColumnLineages, l => (l.OutputAlias ?? l.OutputColumn) == "amount_group" && l.Transformation == TransformationType.Case);
        Assert.Contains(result.ColumnLineages, l => (l.OutputAlias ?? l.OutputColumn) == "total_amount" && l.Transformation == TransformationType.Aggregate);
        Assert.Contains(result.ColumnLineages, l => (l.OutputAlias ?? l.OutputColumn) == "cnt" && l.Transformation == TransformationType.Aggregate);
    }

    [Fact]
    public void Analyze_GroupByQuery_ReturnsGroupByColumns()
    {
        var sql = """
            SELECT 
                c.category_name,
                p.supplier_id,
                COUNT(*) AS product_count
            FROM dbo.Products p
            INNER JOIN dbo.Categories c ON p.category_id = c.category_id
            GROUP BY c.category_name, p.supplier_id
        """;

        // Test without deduplication first
        var resultNoDedup = _analyzer.Analyze(sql, new AnalysisOptions { DeduplicateResults = false });
        Assert.False(resultNoDedup.HasErrors);
        
        // Test with deduplication
        var result = _analyzer.Analyze(sql, Options);
        Assert.False(result.HasErrors);

        Assert.True(result.GroupByColumns.Count >= 2, $"Expected at least 2 GROUP BY columns, got {result.GroupByColumns.Count}");
        Assert.Contains(result.GroupByColumns, c => c.TableAlias == "c" && c.ColumnName == "category_name");
        Assert.Contains(result.GroupByColumns, c => c.TableAlias == "p" && c.ColumnName == "supplier_id");
    }

    [Fact]
    public void ValidateSyntax_InvalidSql_ReturnsErrors()
    {
        var (isValid, errors) = _analyzer.ValidateSyntax("SELECT FROM WHERE");
        Assert.False(isValid);
        Assert.NotEmpty(errors);
    }

    [Fact]  
    public void Debug_GroupByVisitor_DirectTest()
    {
        var sql = """
            SELECT 
                c.category_name,
                p.supplier_id,
                COUNT(*) AS product_count
            FROM dbo.Products p
            INNER JOIN dbo.Categories c ON p.category_id = c.category_id
            GROUP BY c.category_name, p.supplier_id
        """;

        // Parse the SQL directly
        var parser = new Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser(true);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out var errors);
        
        Assert.Empty(errors);
        
        // Test the visitor directly
        var groupByVisitor = new SqlQueryAnalyzer.Visitors.GroupByColumnVisitor();
        fragment.Accept(groupByVisitor);
        
        // Debug: what did we find?
        var output = new StringBuilder();
        output.AppendLine($"Found {groupByVisitor.Columns.Count} GROUP BY columns:");
        foreach (var col in groupByVisitor.Columns)
        {
            output.AppendLine($"  - {col.TableAlias}.{col.ColumnName}");
        }
        
        // This will show in test output
        Assert.True(groupByVisitor.Columns.Count >= 2, output.ToString());
    }
}
