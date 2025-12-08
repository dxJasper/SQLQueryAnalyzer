using SqlQueryAnalyzer.Models;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SqlQueryAnalyzer.Tests;

public class FinalQueryColumnsTests
{
    private readonly SqlQueryAnalyzerService _analyzer = new();
    private static readonly AnalysisOptions Options = new() { DeduplicateResults = true, IncludeInnerTables = false };

    [Test]
    public async Task FinalQueryColumns_SimpleSelect_ReturnsCorrectColumns()
    {
        const string sql = """
                               SELECT 
                                   p.product_id,
                                   p.name,
                                   p.price
                               FROM Products p
                           """;

        var result = _analyzer.Analyze(sql, Options);
        
        await Assert.That(result.HasErrors).IsFalse();
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(3);
        await Assert.That(result.FinalQueryColumns.Any(c => c is { TableAlias: "p", ColumnName: "product_id" })).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c is { TableAlias: "p", ColumnName: "name" })).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c is { TableAlias: "p", ColumnName: "price" })).IsTrue();
    }

    [Test]
    public async Task FinalQueryColumns_SelectWithAliases_ReturnsCorrectColumns()
    {
        const string sql = """
                               SELECT 
                                   p.product_id AS id,
                                   p.name AS product_name,
                                   'Active' AS status
                               FROM Products p
                           """;

        var result = _analyzer.Analyze(sql, Options);
        
        await Assert.That(result.HasErrors).IsFalse();
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(3);
        await Assert.That(result.FinalQueryColumns.Any(c => c is { TableAlias: "p", ColumnName: "product_id", Alias: "id" })).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c is { TableAlias: "p", ColumnName: "name", Alias: "product_name" })).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c is { ColumnName: "[Expression]", Alias: "status" })).IsTrue();
    }

    [Test]
    public async Task FinalQueryColumns_CteQuery_OnlyReturnsFinalQueryColumns()
    {
        const string sql = """
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
        
        await Assert.That(result.HasErrors).IsFalse();

        // FinalQueryColumns should only have the 2 columns from the final SELECT
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(2);
        await Assert.That(result.FinalQueryColumns.Any(c => c is { TableAlias: "ac", ColumnName: "customer_id" })).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c is { TableAlias: "ac", ColumnName: "name" })).IsTrue();

        // Should NOT contain email or phone from the CTE definition
        await Assert.That(result.FinalQueryColumns.Any(c => c.ColumnName == "email")).IsFalse();
        await Assert.That(result.FinalQueryColumns.Any(c => c.ColumnName == "phone")).IsFalse();

        // SelectColumns should contain all columns (including CTE columns)
        await Assert.That(result.SelectColumns.Count).IsGreaterThan(result.FinalQueryColumns.Count);
    }

    [Test]
    public async Task FinalQueryColumns_MultipleCtesQuery_OnlyReturnsFinalQueryColumns()
    {
        const string sql = """
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
        
        await Assert.That(result.HasErrors).IsFalse();

        // FinalQueryColumns should only have the 3 columns from the final SELECT
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(3);
        await Assert.That(result.FinalQueryColumns.Any(c => c is { TableAlias: "ac", ColumnName: "customer_id" })).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c is { TableAlias: "ac", ColumnName: "name" })).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c is { TableAlias: "ro", ColumnName: "order_count" })).IsTrue();

        // Should NOT contain other CTE definition columns
        await Assert.That(result.FinalQueryColumns.Any(c => c.ColumnName == "email")).IsFalse();
        await Assert.That(result.FinalQueryColumns.Any(c => c.ColumnName == "last_order")).IsFalse();
    }

    [Test]
    public async Task FinalQueryColumns_SubqueryInSelect_OnlyReturnsFinalQueryColumns()
    {
        const string sql = """
                               SELECT 
                                   p.product_id,
                                   p.name,
                                   (SELECT AVG(price) FROM dbo.Products) as avg_price
                               FROM dbo.Products p
                           """;

        var result = _analyzer.Analyze(sql, Options);
        
        await Assert.That(result.HasErrors).IsFalse();

        // FinalQueryColumns should only have the 3 columns from the outer SELECT
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(3);
        await Assert.That(result.FinalQueryColumns.Any(c => c is { TableAlias: "p", ColumnName: "product_id" })).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c is { TableAlias: "p", ColumnName: "name" })).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c is { Alias: "avg_price", Kind: ColumnKind.Subquery })).IsTrue();
    }

    [Test]
    public async Task FinalQueryColumns_ComplexQuery_OnlyReturnsFinalQueryColumns()
    {
        const string sql = """
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
        
        await Assert.That(result.HasErrors).IsFalse();

        // Should have exactly 5 output columns
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(5);
        await Assert.That(result.FinalQueryColumns.Any(c => c is { TableAlias: "c", ColumnName: "category_name" })).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c is { TableAlias: "p", ColumnName: "supplier_id" })).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c is { Alias: "product_count", Kind: ColumnKind.Aggregate })).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c is { Alias: "total_value", Kind: ColumnKind.Aggregate })).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c is { Alias: "avg_price", Kind: ColumnKind.Aggregate })).IsTrue();
    }

    [Test]
    public async Task FinalQueryColumns_SelectStar_ReturnsStarColumn()
    {
        const string sql = "SELECT * FROM Products p";

        var result = _analyzer.Analyze(sql, Options);
        
        await Assert.That(result.HasErrors).IsFalse();
        await Assert.That(result.FinalQueryColumns).HasSingleItem();
        await Assert.That(result.FinalQueryColumns.Any(c => c is { ColumnName: "*", Kind: ColumnKind.Star })).IsTrue();
    }

    [Test]
    public async Task FinalQueryColumns_QualifiedSelectStar_ReturnsQualifiedStarColumn()
    {
        const string sql = """
                               SELECT p.*
                               FROM Products p
                               JOIN Categories c ON p.category_id = c.category_id
                           """;

        var result = _analyzer.Analyze(sql, Options);
        
        await Assert.That(result.HasErrors).IsFalse();
        await Assert.That(result.FinalQueryColumns).HasSingleItem();
        await Assert.That(result.FinalQueryColumns.Any(c => c is { TableAlias: "p", ColumnName: "*", Kind: ColumnKind.Star })).IsTrue();
    }

    [Test]
    public async Task FinalQueryColumns_EmptyResult_ForInvalidSql()
    {
        var sql = "SELECT FROM WHERE";

        var result = _analyzer.Analyze(sql, Options);
        
        await Assert.That(result.HasErrors).IsTrue();
        await Assert.That(result.FinalQueryColumns).IsEmpty();
    }

    [Test]
    public async Task FinalQueryColumns_DifferentFromSelectColumns_WithCte()
    {
        const string sql = """
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
        
        await Assert.That(result.HasErrors).IsFalse();

        // FinalQueryColumns should only have 2 columns
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(2);

        // SelectColumns should have more (including CTE definition columns)
        await Assert.That(result.SelectColumns.Count).IsGreaterThan(result.FinalQueryColumns.Count);

        // Verify the final columns are correct  
        await Assert.That(result.FinalQueryColumns.All(c => c.TableAlias == "td")).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c.ColumnName == "id")).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c.ColumnName == "name")).IsTrue();
    }

    [Test]
    public async Task FinalQueryColumns_UnpivotQuery_ReturnsExpectedColumns()
    {
        const string sql = """
                           DECLARE @MOGID AS NVARCHAR(255) = (
                           SELECT        v.Value
                           FROM    DCA.Variable AS v
                           WHERE    v.Name = 'MOGID'
                                                           )
                           SELECT        up.col AS assettype
                           ,            AAPT.migrate AS migrate
                           ,            AAPT.accountType AS accountType
                           ,            up.value AS amount
                           ,            AAPT.postingType AS postingType
                           ,            peildatum.valueDate
                           FROM        AFL.CalculatedPostingAmount CPA
                               UNPIVOT (
                           value
                           FOR col IN ( budget_inhaalindexatie, budget_standaardregel, budget_aanvulling_tv
                                                   , budget_compensatiedepot, solidariteitsreserve, solidariteitsreserve_initieel
                                                   , solidariteitsreserve_delta, operationele_reserve, kostenvoorziening
                                                   , kostenvoorziening_initieel, kostenvoorziening_delta, wezenpensioen_voorziening
                                                   , wezenpensioen_voorziening_initieel, wezenpensioen_voorziening_delta, pvao_voorziening
                                                   , pvao_voorziening_initieel, pvao_voorziening_delta, ibnr_aop_voorziening
                                                   , ibnr_aop_voorziening_initieel, ibnr_aop_voorziening_delta, ibnr_pvao_voorziening
                                                   , ibnr_pvao_voorziening_initieel, ibnr_pvao_voorziening_delta, totaal_fondsvermogen
                                                   , totaal_fondsvermogen_initieel, totaal_fondsvermogen_delta
                                                   )
                                       ) up
                           LEFT JOIN    VRT.AccountAndPostingType AAPT
                           ON AAPT.vermogensOnderdeel = up.col
                           AND AAPT.MOGID = @MOGID
                           CROSS APPLY (
                           SELECT    MAX(lvpkc.PEILDATUMFUNC) AS valueDate
                           FROM    DK.L33_V_PVS_KLANT_CONTACTPUNT AS lvpkc
                                       ) AS peildatum
                           """;

        var result = _analyzer.Analyze(sql, Options);

        // The query parses successfully!
        await Assert.That(result.HasErrors).IsFalse();

        // Validate the expected 6 FinalQueryColumns as specified
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(6);

        // Also verify SelectColumns includes the subquery SELECT (8 total)
        await Assert.That(result.SelectColumns.Count).IsEqualTo(8);

        // Verify the distinction: FinalQueryColumns < SelectColumns for this complex query
        await Assert.That(result.FinalQueryColumns.Count).IsLessThan(result.SelectColumns.Count);

        // Verify the specific output columns exist (without being too specific about table aliases)
        await Assert.That(result.FinalQueryColumns.Any(c => c.Alias == "assettype")).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c.Alias == "migrate")).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c.Alias == "accountType")).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c.Alias == "amount")).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c.Alias == "postingType")).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c.ColumnName == "valueDate")).IsTrue();
    }
}