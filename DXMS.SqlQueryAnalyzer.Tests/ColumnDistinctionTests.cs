namespace DXMS.SqlQueryAnalyzer.Tests;

/// <summary>
/// Tests to ensure proper distinction between SelectColumns and FinalQueryColumns
/// FinalQueryColumns should only include columns from the outermost SELECT statement
/// that produces the actual query result, excluding inner CTE and subquery columns
/// </summary>
public class ColumnDistinctionTests
{
    private readonly SqlQueryAnalyzerService _analyzer = new();

    [Test]
    public async Task SimpleQuery_SelectAndFinalColumnsAreEqual()
    {
        var sql = "SELECT p.id, p.name FROM Products p";

        var result = _analyzer.Analyze(sql);

        await Assert.That(result.HasErrors).IsFalse();
        await Assert.That(result.SelectColumns.Count).IsEqualTo(result.FinalQueryColumns.Count);
        await Assert.That(result.SelectColumns.Count).IsEqualTo(2);
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(2);
    }

    [Test]
    public async Task CteQuery_FinalColumnsAreLessThanSelectColumns()
    {
        var sql = """
            WITH TempData AS (
                SELECT id, name, email, created_date
                FROM Users
            )
            SELECT td.id, td.name
            FROM TempData td
            """;

        var result = _analyzer.Analyze(sql);

        await Assert.That(result.HasErrors).IsFalse();

        // SelectColumns includes CTE columns (6 total: 4 from CTE + 2 from final SELECT)
        await Assert.That(result.SelectColumns.Count).IsEqualTo(6);

        // FinalQueryColumns includes only final SELECT (2 columns)
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(2);

        // Key assertion: FinalQueryColumns < SelectColumns
        await Assert.That(result.FinalQueryColumns.Count).IsLessThan(result.SelectColumns.Count);
    }

    [Test]
    public async Task SubqueryInSelect_FinalColumnsAreLessThanSelectColumns()
    {
        var sql = """
            SELECT 
                p.id, 
                p.name,
                (SELECT COUNT(*) FROM Orders WHERE customer_id = p.id) as order_count
            FROM Products p
            """;

        var result = _analyzer.Analyze(sql);

        await Assert.That(result.HasErrors).IsFalse();

        // SelectColumns includes subquery columns (4 total: 3 from main + 1 from subquery)
        await Assert.That(result.SelectColumns.Count).IsEqualTo(4);

        // FinalQueryColumns includes only main SELECT (3 columns)
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(3);

        // Key assertion: FinalQueryColumns < SelectColumns
        await Assert.That(result.FinalQueryColumns.Count).IsLessThan(result.SelectColumns.Count);
    }

    [Test]
    public async Task ComplexNestedQuery_MaintainsCorrectDistinction()
    {
        var sql = """
            WITH CategoryStats AS (
                SELECT 
                    category_id, 
                    COUNT(*) as product_count,
                    AVG(price) as avg_price,
                    MIN(price) as min_price,
                    MAX(price) as max_price
                FROM Products 
                GROUP BY category_id
            )
            SELECT 
                cs.category_id,
                cs.product_count,
                (SELECT name FROM Categories c WHERE c.id = cs.category_id) as category_name
            FROM CategoryStats cs
            WHERE cs.product_count > 10
            """;

        var result = _analyzer.Analyze(sql);

        await Assert.That(result.HasErrors).IsFalse();

        // SelectColumns includes CTE + main + subquery columns (5 + 3 + 1 = 9 total)
        await Assert.That(result.SelectColumns.Count).IsEqualTo(9);

        // FinalQueryColumns includes only final SELECT (3 columns)
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(3);

        // Key assertion: FinalQueryColumns should be much less than SelectColumns
        await Assert.That(result.FinalQueryColumns.Count).IsLessThan(result.SelectColumns.Count);

        // Specific ratio check: FinalQueryColumns should be 1/3 of SelectColumns in this case
        await Assert.That(result.FinalQueryColumns.Count * 3).IsEqualTo(result.SelectColumns.Count);
    }

    [Test]
    public async Task MultipleNestedLevels_OnlyOutermostInFinalColumns()
    {
        var sql = """
            WITH Level1 AS (
                SELECT id, name, category_id FROM Products
            ),
            Level2 AS (
                SELECT 
                    l1.id,
                    l1.name,
                    (SELECT COUNT(*) FROM Orders WHERE product_id = l1.id) as order_count
                FROM Level1 l1
            )
            SELECT l2.id, l2.name
            FROM Level2 l2
            WHERE l2.order_count > 5
            """;

        var result = _analyzer.Analyze(sql);

        await Assert.That(result.HasErrors).IsFalse();

        // SelectColumns includes all levels (3 + 3 + 1 + 2 = 9 total)
        await Assert.That(result.SelectColumns.Count).IsEqualTo(9);

        // FinalQueryColumns includes only outermost SELECT (2 columns)
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(2);
    }

    [Test]
    public async Task UniversalRule_FinalNeverExceedsSelect()
    {
        var testQueries = new[]
        {
            "SELECT id FROM Users",
            "SELECT id, name FROM Users",
            "WITH T AS (SELECT id FROM Users) SELECT id FROM T",
            "SELECT id, (SELECT COUNT(*) FROM Orders) as cnt FROM Users",
            "WITH T1 AS (SELECT id FROM Users), T2 AS (SELECT id FROM Products) SELECT T1.id FROM T1"
        };

        foreach (var query in testQueries)
        {
            var result = _analyzer.Analyze(query);
            await Assert.That(result.HasErrors).IsFalse();

            // Universal rule: FinalQueryColumns should NEVER exceed SelectColumns
            await Assert.That(result.FinalQueryColumns.Count).IsLessThanOrEqualTo(result.SelectColumns.Count);

            // Both should be > 0 for valid queries
            await Assert.That(result.FinalQueryColumns.Count).IsGreaterThan(0);
            await Assert.That(result.SelectColumns.Count).IsGreaterThan(0);
        }
    }
}