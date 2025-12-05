using SqlQueryAnalyzer;
using SqlQueryAnalyzer.Models;
using Xunit;

namespace SqlQueryAnalyzer.Tests;

public class FinalQueryColumnsTests
{
    private readonly SqlQueryAnalyzerService _analyzer = new();
    private static readonly AnalysisOptions Options = new() { DeduplicateResults = true, IncludeInnerTables = false };

    [Fact]
    public void FinalQueryColumns_SimpleSelect_ReturnsCorrectColumns()
    {
        var sql = """
            SELECT 
                p.product_id,
                p.name,
                p.price
            FROM Products p
        """;

        var result = _analyzer.Analyze(sql, Options);
        Assert.False(result.HasErrors);

        Assert.Equal(3, result.FinalQueryColumns.Count);
        Assert.Contains(result.FinalQueryColumns, c => c.TableAlias == "p" && c.ColumnName == "product_id");
        Assert.Contains(result.FinalQueryColumns, c => c.TableAlias == "p" && c.ColumnName == "name");
        Assert.Contains(result.FinalQueryColumns, c => c.TableAlias == "p" && c.ColumnName == "price");
    }

    [Fact]
    public void FinalQueryColumns_SelectWithAliases_ReturnsCorrectColumns()
    {
        var sql = """
            SELECT 
                p.product_id AS id,
                p.name AS product_name,
                'Active' AS status
            FROM Products p
        """;

        var result = _analyzer.Analyze(sql, Options);
        Assert.False(result.HasErrors);

        Assert.Equal(3, result.FinalQueryColumns.Count);
        Assert.Contains(result.FinalQueryColumns, c => c.TableAlias == "p" && c.ColumnName == "product_id" && c.Alias == "id");
        Assert.Contains(result.FinalQueryColumns, c => c.TableAlias == "p" && c.ColumnName == "name" && c.Alias == "product_name");
        Assert.Contains(result.FinalQueryColumns, c => c.ColumnName == "[Expression]" && c.Alias == "status");
    }

    [Fact]
    public void FinalQueryColumns_CteQuery_OnlyReturnsFinalQueryColumns()
    {
        var sql = """
            WITH ActiveCustomers AS (
                SELECT customer_id, name, email, phone
                FROM dbo.Customers
                WHERE status = 'Active'
            )
            SELECT 
                ac.customer_id,
                ac.name
            FROM ActiveCustomers ac
        """;

        var result = _analyzer.Analyze(sql, Options);
        Assert.False(result.HasErrors);

        // FinalQueryColumns should only have the 2 columns from the final SELECT
        Assert.Equal(2, result.FinalQueryColumns.Count);
        Assert.Contains(result.FinalQueryColumns, c => c.TableAlias == "ac" && c.ColumnName == "customer_id");
        Assert.Contains(result.FinalQueryColumns, c => c.TableAlias == "ac" && c.ColumnName == "name");
        
        // Should NOT contain email or phone from the CTE definition
        Assert.DoesNotContain(result.FinalQueryColumns, c => c.ColumnName == "email");
        Assert.DoesNotContain(result.FinalQueryColumns, c => c.ColumnName == "phone");

        // SelectColumns should contain all columns (including CTE columns)
        Assert.True(result.SelectColumns.Count > result.FinalQueryColumns.Count);
    }

    [Fact]
    public void FinalQueryColumns_MultipleCtesQuery_OnlyReturnsFinalQueryColumns()
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
                ro.order_count
            FROM ActiveCustomers ac
            LEFT JOIN RecentOrders ro ON ac.customer_id = ro.customer_id
        """;

        var result = _analyzer.Analyze(sql, Options);
        Assert.False(result.HasErrors);

        // FinalQueryColumns should only have the 3 columns from the final SELECT
        Assert.Equal(3, result.FinalQueryColumns.Count);
        Assert.Contains(result.FinalQueryColumns, c => c.TableAlias == "ac" && c.ColumnName == "customer_id");
        Assert.Contains(result.FinalQueryColumns, c => c.TableAlias == "ac" && c.ColumnName == "name");
        Assert.Contains(result.FinalQueryColumns, c => c.TableAlias == "ro" && c.ColumnName == "order_count");
        
        // Should NOT contain other CTE definition columns
        Assert.DoesNotContain(result.FinalQueryColumns, c => c.ColumnName == "email");
        Assert.DoesNotContain(result.FinalQueryColumns, c => c.ColumnName == "last_order");
    }

    [Fact]
    public void FinalQueryColumns_SubqueryInSelect_OnlyReturnsFinalQueryColumns()
    {
        var sql = """
            SELECT 
                p.product_id,
                p.name,
                (SELECT AVG(price) FROM dbo.Products) as avg_price
            FROM dbo.Products p
        """;

        var result = _analyzer.Analyze(sql, Options);
        Assert.False(result.HasErrors);

        // FinalQueryColumns should only have the 3 columns from the outer SELECT
        Assert.Equal(3, result.FinalQueryColumns.Count);
        Assert.Contains(result.FinalQueryColumns, c => c.TableAlias == "p" && c.ColumnName == "product_id");
        Assert.Contains(result.FinalQueryColumns, c => c.TableAlias == "p" && c.ColumnName == "name");
        Assert.Contains(result.FinalQueryColumns, c => c.Alias == "avg_price" && c.Kind == ColumnKind.Subquery);
    }

    [Fact]
    public void FinalQueryColumns_ComplexQuery_OnlyReturnsFinalQueryColumns()
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
            WHERE p.discontinued = 0
            GROUP BY c.category_name, p.supplier_id
            HAVING COUNT(*) > 5
            ORDER BY total_value DESC
        """;

        var result = _analyzer.Analyze(sql, Options);
        Assert.False(result.HasErrors);

        // Should have exactly 5 output columns
        Assert.Equal(5, result.FinalQueryColumns.Count);
        Assert.Contains(result.FinalQueryColumns, c => c.TableAlias == "c" && c.ColumnName == "category_name");
        Assert.Contains(result.FinalQueryColumns, c => c.TableAlias == "p" && c.ColumnName == "supplier_id");
        Assert.Contains(result.FinalQueryColumns, c => c.Alias == "product_count" && c.Kind == ColumnKind.Aggregate);
        Assert.Contains(result.FinalQueryColumns, c => c.Alias == "total_value" && c.Kind == ColumnKind.Aggregate);
        Assert.Contains(result.FinalQueryColumns, c => c.Alias == "avg_price" && c.Kind == ColumnKind.Aggregate);
    }

    [Fact]
    public void FinalQueryColumns_SelectStar_ReturnsStarColumn()
    {
        var sql = "SELECT * FROM Products p";

        var result = _analyzer.Analyze(sql, Options);
        Assert.False(result.HasErrors);

        Assert.Single(result.FinalQueryColumns);
        Assert.Contains(result.FinalQueryColumns, c => c.ColumnName == "*" && c.Kind == ColumnKind.Star);
    }

    [Fact]
    public void FinalQueryColumns_QualifiedSelectStar_ReturnsQualifiedStarColumn()
    {
        var sql = """
            SELECT p.*
            FROM Products p
            JOIN Categories c ON p.category_id = c.category_id
        """;

        var result = _analyzer.Analyze(sql, Options);
        Assert.False(result.HasErrors);

        Assert.Single(result.FinalQueryColumns);
        Assert.Contains(result.FinalQueryColumns, c => c.TableAlias == "p" && c.ColumnName == "*" && c.Kind == ColumnKind.Star);
    }

    [Fact]
    public void FinalQueryColumns_EmptyResult_ForInvalidSql()
    {
        var sql = "SELECT FROM WHERE";

        var result = _analyzer.Analyze(sql, Options);
        Assert.True(result.HasErrors);
        Assert.Empty(result.FinalQueryColumns);
    }

    [Fact]
    public void FinalQueryColumns_DifferentFromSelectColumns_WithCte()
    {
        var sql = """
            WITH TempData AS (
                SELECT id, name, status, created_date, modified_date
                FROM Users
                WHERE active = 1
            )
            SELECT 
                td.id,
                td.name
            FROM TempData td
        """;

        var result = _analyzer.Analyze(sql, Options);
        Assert.False(result.HasErrors);

        // FinalQueryColumns should only have 2 columns
        Assert.Equal(2, result.FinalQueryColumns.Count);
        
        // SelectColumns should have more (including CTE definition columns)
        Assert.True(result.SelectColumns.Count > result.FinalQueryColumns.Count);
        
        // Verify the final columns are correct
        Assert.True(result.FinalQueryColumns.All(c => c.TableAlias == "td"));
        Assert.Contains(result.FinalQueryColumns, c => c.ColumnName == "id");
        Assert.Contains(result.FinalQueryColumns, c => c.ColumnName == "name");
    }
}