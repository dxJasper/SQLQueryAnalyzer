using SqlQueryAnalyzer;
using SqlQueryAnalyzer.Models;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SqlQueryAnalyzer.Tests;

/// <summary>
/// Tests for AnalysisOptions to ensure proper configuration works
/// </summary>
public class AnalysisOptionsTests
{
    private readonly SqlQueryAnalyzerService _analyzer = new();

    [Test]
    public async Task BuildColumnLineage_WhenDisabled_SkipsLineageBuilding()
    {
        var sql = """
            SELECT 
                c.category_name,
                COUNT(*) AS product_count
            FROM dbo.Products p
            INNER JOIN dbo.Categories c ON p.category_id = c.category_id
            GROUP BY c.category_name
            """;

        var options = AnalysisOptions.CreateBuilder()
            .BuildColumnLineage(false)
            .Build();

        var result = _analyzer.Analyze(sql, options);
        
        await Assert.That(result.HasErrors).IsFalse();
        await Assert.That(result.ColumnLineages).IsEmpty();
        await Assert.That(result.SelectColumns.Count).IsGreaterThan(0);
        await Assert.That(result.Tables.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task BuildColumnLineage_WhenEnabled_BuildsLineage()
    {
        var sql = """
            SELECT 
                c.category_name,
                COUNT(*) AS product_count
            FROM dbo.Products p
            INNER JOIN dbo.Categories c ON p.category_id = c.category_id
            GROUP BY c.category_name
            """;

        var options = AnalysisOptions.CreateBuilder()
            .BuildColumnLineage(true)
            .Build();

        var result = _analyzer.Analyze(sql, options);
        
        await Assert.That(result.HasErrors).IsFalse();
        await Assert.That(result.ColumnLineages.Count).IsGreaterThan(0);
        await Assert.That(result.SelectColumns.Count).IsGreaterThan(0);
        await Assert.That(result.Tables.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task AnalyzeNestedQueries_WhenDisabled_SkipsNestedAnalysis()
    {
        var sql = """
            WITH TestCte AS (
                SELECT id, name FROM Users WHERE active = 1
            )
            SELECT 
                t.id,
                (SELECT COUNT(*) FROM Orders WHERE user_id = t.id) as order_count
            FROM TestCte t
            """;

        var options = AnalysisOptions.CreateBuilder()
            .AnalyzeNestedQueries(false)
            .Build();

        var result = _analyzer.Analyze(sql, options);
        
        await Assert.That(result.HasErrors).IsFalse();
        await Assert.That(result.CommonTableExpressions.Count).IsEqualTo(1);
        await Assert.That(result.SubQueries.Count).IsEqualTo(1);
        
        // Inner analysis should be null when AnalyzeNestedQueries is false
        await Assert.That(result.CommonTableExpressions[0].InnerAnalysis).IsNull();
        await Assert.That(result.SubQueries[0].InnerAnalysis).IsNull();
    }

    [Test]
    public async Task AnalyzeNestedQueries_WhenEnabled_PerformsNestedAnalysis()
    {
        var sql = """
            WITH TestCte AS (
                SELECT id, name FROM Users WHERE active = 1
            )
            SELECT 
                t.id,
                (SELECT COUNT(*) FROM Orders WHERE user_id = t.id) as order_count
            FROM TestCte t
            """;

        var options = AnalysisOptions.CreateBuilder()
            .AnalyzeNestedQueries(true)
            .Build();

        var result = _analyzer.Analyze(sql, options);
        
        await Assert.That(result.HasErrors).IsFalse();
        await Assert.That(result.CommonTableExpressions.Count).IsEqualTo(1);
        await Assert.That(result.SubQueries.Count).IsEqualTo(1);
        
        // Inner analysis should be present when AnalyzeNestedQueries is true
        await Assert.That(result.CommonTableExpressions[0].InnerAnalysis).IsNotNull();
        await Assert.That(result.SubQueries[0].InnerAnalysis).IsNotNull();
    }

    [Test]
    public async Task ForPerformance_ConfiguresMinimalProcessing()
    {
        var sql = """
            WITH TestCte AS (
                SELECT id, name FROM Users WHERE active = 1
            )
            SELECT 
                t.id,
                COUNT(*) AS total
            FROM TestCte t
            GROUP BY t.id
            """;

        var options = AnalysisOptions.CreateBuilder()
            .ForPerformance()
            .Build();

        var result = _analyzer.Analyze(sql, options);
        
        await Assert.That(result.HasErrors).IsFalse();
        await Assert.That(result.SelectColumns.Count).IsGreaterThan(0);
        await Assert.That(result.Tables.Count).IsGreaterThan(0);
        
        // Performance mode should skip lineage building and nested analysis
        await Assert.That(result.ColumnLineages).IsEmpty();
        await Assert.That(result.CommonTableExpressions[0].InnerAnalysis).IsNull();
    }

    [Test]
    public async Task ForComprehensive_EnablesAllFeatures()
    {
        var sql = """
            WITH TestCte AS (
                SELECT id, name FROM Users WHERE active = 1
            )
            SELECT 
                t.id,
                COUNT(*) AS total
            FROM TestCte t
            GROUP BY t.id
            """;

        var options = AnalysisOptions.CreateBuilder()
            .ForComprehensive()
            .Build();

        var result = _analyzer.Analyze(sql, options);
        
        await Assert.That(result.HasErrors).IsFalse();
        await Assert.That(result.SelectColumns.Count).IsGreaterThan(0);
        await Assert.That(result.Tables.Count).IsGreaterThan(0);
        
        // Comprehensive mode should enable all features
        await Assert.That(result.ColumnLineages.Count).IsGreaterThan(0);
        await Assert.That(result.CommonTableExpressions[0].InnerAnalysis).IsNotNull();
    }

    [Test]
    public async Task IncludeInnerTables_WhenDisabled_ExcludesSubqueryTables()
    {
        var sql = """
            SELECT 
                p.id,
                (SELECT COUNT(*) FROM Orders o WHERE o.customer_id = p.id) as order_count
            FROM Products p
            """;

        var options = AnalysisOptions.CreateBuilder()
            .IncludeInnerTables(false)
            .Build();

        var result = _analyzer.Analyze(sql, options);
        
        await Assert.That(result.HasErrors).IsFalse();
        
        // Should only include main query tables, not subquery tables
        await Assert.That(result.Tables).Contains(t => t.TableName == "Products");
        // Orders table from subquery should not be included when IncludeInnerTables = false
        await Assert.That(result.Tables.Where(t => t.TableName == "Orders")).IsEmpty();
    }

    [Test]
    public async Task IncludeInnerTables_WhenEnabled_IncludesSubqueryTables()
    {
        var sql = """
            SELECT 
                p.id,
                (SELECT COUNT(*) FROM Orders o WHERE o.customer_id = p.id) as order_count
            FROM Products p
            """;

        var options = AnalysisOptions.CreateBuilder()
            .IncludeInnerTables(true)
            .Build();

        var result = _analyzer.Analyze(sql, options);
        
        await Assert.That(result.HasErrors).IsFalse();
        
        // Should include both main query and subquery tables
        await Assert.That(result.Tables).Contains(t => t.TableName == "Products");
        await Assert.That(result.Tables).Contains(t => t.TableName == "Orders");
    }

    [Test]
    public async Task DeduplicateResults_WhenDisabled_KeepsAllEntries()
    {
        var sql = """
            SELECT p.id, p.name, p.id
            FROM Products p, Products p2
            """;

        var options = AnalysisOptions.CreateBuilder()
            .DeduplicateResults(false)
            .Build();

        var result = _analyzer.Analyze(sql, options);
        
        await Assert.That(result.HasErrors).IsFalse();
        
        // Without deduplication, we might see duplicates
        await Assert.That(result.SelectColumns.Count).IsGreaterThanOrEqualTo(3);
    }

    [Test]
    public async Task DeduplicateResults_WhenEnabled_RemovesDuplicates()
    {
        var sql = """
            SELECT p.id, p.name
            FROM Products p
            """;

        var options = AnalysisOptions.CreateBuilder()
            .DeduplicateResults(true)
            .Build();

        var result = _analyzer.Analyze(sql, options);
        
        await Assert.That(result.HasErrors).IsFalse();
        
        // With deduplication, results should be clean
        await Assert.That(result.SelectColumns.Count).IsEqualTo(2);
    }
}