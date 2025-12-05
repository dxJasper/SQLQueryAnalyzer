using SqlQueryAnalyzer.Models;
using Xunit;

namespace SqlQueryAnalyzer.Tests;

public class ComplexQueriesTests
{
    private readonly SqlQueryAnalyzerService _analyzer = new();
    private static readonly AnalysisOptions Options = new() { IncludeInnerTables = false, DeduplicateResults = true };

    [Fact]
    public void Analyze_ComplexJoinGroupOrder_Query_ReturnsExpectedStructures()
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
                               LEFT JOIN dbo.Suppliers s ON p.supplier_id = s.supplier_id
                               WHERE p.discontinued = 0 AND p.unit_price > 10
                               GROUP BY c.category_name, p.supplier_id
                               HAVING COUNT(*) > 5
                               ORDER BY total_value DESC, c.category_name ASC
                           """;

        var result = _analyzer.Analyze(sql, Options);
        Assert.False(result.HasErrors);

        Assert.Contains(result.Tables, t => t is { TableName: "Products", Alias: "p" });
        Assert.Contains(result.Tables, t => t is { TableName: "Categories", Alias: "c" });
        Assert.Contains(result.Tables, t => t is { TableName: "Suppliers", Alias: "s" });

        Assert.Contains(result.SelectColumns, c => c is { ColumnName: "category_name", TableAlias: "c" });
        Assert.Contains(result.SelectColumns, c => c is { ColumnName: "supplier_id", TableAlias: "p" });
        Assert.Contains(result.SelectColumns, c => c.Alias == "product_count");
        Assert.Contains(result.OrderByColumns, c => c is { ColumnName: "total_value", IsAscending: false });

        Assert.Contains(result.ColumnLineages, l => (l.OutputAlias ?? l.OutputColumn) == "avg_price" && l.Transformation == TransformationType.Aggregate);
    }

    [Fact]
    public void Analyze_CteAndInnerQueries_ReturnsCteAndTopLevelTables()
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

        Assert.Contains(result.Tables, t => t is { Type: TableReferenceType.Cte, TableName: "ActiveCustomers" });
        Assert.Contains(result.Tables, t => t is { Type: TableReferenceType.Cte, TableName: "RecentOrders" });

        Assert.Contains(result.SelectColumns, c => c is { TableAlias: "ac", ColumnName: "customer_id" });
        Assert.Contains(result.SelectColumns, c => c is { TableAlias: "ro", ColumnName: "order_count" });

        Assert.Contains(result.ColumnLineages, l => (l.OutputAlias ?? l.OutputColumn) == "last_order");
    }

    [Fact]
    public void Analyze_Subqueries_CollectsSubqueryInfoAndTables()
    {
        const string sql = """
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

        Assert.Contains(result.Tables, t => t is { TableName: "Products", Alias: "p" });
        Assert.Contains(result.SubQueries, s => s.Type == SubQueryType.ScalarSubquery);
        Assert.Contains(result.SubQueries, s => s.Type == SubQueryType.InSubquery);
        Assert.Contains(result.SubQueries, s => s.Type == SubQueryType.ExistsSubquery);

        Assert.Contains(result.ColumnLineages, l => (l.OutputAlias ?? l.OutputColumn) == "avg_price" && l.Transformation == TransformationType.Subquery);
    }

    [Fact]
    public void Analyze_CaseExpressionsAndAggregates_LineageAndColumnsDetected()
    {
        const string sql = """
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
        const string sql = """
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
        Assert.Contains(result.GroupByColumns, c => c is { TableAlias: "c", ColumnName: "category_name" });
        Assert.Contains(result.GroupByColumns, c => c is { TableAlias: "p", ColumnName: "supplier_id" });
    }

    [Fact]
    public void ValidateSyntax_InvalidSql_ReturnsErrors()
    {
        var (isValid, errors) = _analyzer.ValidateSyntax("SELECT FROM WHERE");
        Assert.False(isValid);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Analyze_UnpivotWithDeclareAndCrossApply_ParsesAndAnalyzesCorrectly()
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
        Assert.False(result.HasErrors);

        // Test UNPIVOT table detection
        Assert.Contains(result.Tables, t => t is { TableName: "CalculatedPostingAmount", Alias: "CPA" });

        // Test JOIN detection with complex conditions
        Assert.Contains(result.Tables, t => t is { TableName: "AccountAndPostingType", Alias: "AAPT", JoinType: JoinType.Left });

        // Test CROSS APPLY detection (should be treated as a derived table)
        Assert.Contains(result.Tables, t => t.Alias == "peildatum");

        // Test variable declaration subquery table detection
        Assert.Contains(result.Tables, t => t is { TableName: "Variable", Alias: "v" });

        // Test column analysis - should have 6 output columns
        Assert.True(result.FinalQueryColumns.Count >= 6, $"Expected at least 6 final columns, got {result.FinalQueryColumns.Count}");

        // Test that UNPIVOT columns are properly handled
        Assert.Contains(result.FinalQueryColumns, c => c is { TableAlias: "up", ColumnName: "col" });
        Assert.Contains(result.FinalQueryColumns, c => c is { TableAlias: "up", ColumnName: "value" });

        // Test alias detection
        Assert.Contains(result.FinalQueryColumns, c => c.Alias == "assettype");
        Assert.Contains(result.FinalQueryColumns, c => c.Alias == "amount");

        // Test schema detection from multiple schemas
        var schemas = result.Schemas.ToList();
        Assert.Contains("AFL", schemas);
        Assert.Contains("VRT", schemas);
        Assert.Contains("DCA", schemas);
        Assert.Contains("DK", schemas);

        // Test JOIN column detection
        Assert.True(result.JoinColumns.Count >= 2, "Should detect JOIN columns");
        Assert.Contains(result.JoinColumns, c => c is { TableAlias: "AAPT", ColumnName: "vermogensOnderdeel" });
        Assert.Contains(result.JoinColumns, c => c is { TableAlias: "up", ColumnName: "col" });
    }

    [Fact]
    public void Analyze_ComplexNestedJsonQuery_Returns2FinalColumns()
    {
        const string sql = """
                           SELECT	jsonsel.DX_ID
                           ,		COMPRESS(jsonsel.JSON_BERICHT) AS JSON_BERICHT_Compressed
                           INTO	JSON.Command
                           FROM	(
                           			SELECT	sc_.DX_ID
                           			,		JSON_BERICHT = (
                           								SELECT			'NlMigrationV11CommandData' AS [_type]
                           								,			sc.automaticTransferAttempts AS [automaticTransferAttempts]
                           								,			sc.birthDate AS [birthDate]
                           								,			CASE WHEN DisabilityInformation.DX_ID IS NOT NULL THEN
                           														'NlMigrationV11DisabilityInformation'
                           													ELSE NULL
                           												END AS [disabilityInformation._type]
                           								,			DisabilityInformation.benefitBasisFraction AS [disabilityInformation.benefitBasisFraction]
                           								,			DisabilityInformation.benefitBasisType AS [disabilityInformation.benefitBasisType]
                           								,			DisabilityInformation.benefitType AS [disabilityInformation.benefitType]
                           								,			DisabilityInformation.continuationPercentage AS [disabilityInformation.continuationPercentage]
                           								,			DisabilityInformation.continuationPercentageForAop AS [disabilityInformation.continuationPercentageForAop]
                           								,			DisabilityInformation.countValueForKapCover AS [disabilityInformation.countValueForKapCover]
                           								,			DisabilityInformation.dailyWageUncappedAmount AS [disabilityInformation.dailyWageUncappedAmount]
                           								,			DisabilityInformation.disabilityClassType AS [disabilityInformation.disabilityClassType]
                           								,			DisabilityInformation.disabilityFraction AS [disabilityInformation.disabilityFraction]
                           								,			DisabilityInformation.disabilityType AS [disabilityInformation.disabilityType]
                           								,			DisabilityInformation.entitlementEndDate AS [disabilityInformation.entitlementEndDate]
                           								,			DisabilityInformation.entitlementStartDate AS [disabilityInformation.entitlementStartDate]
                           								,			DisabilityInformation.entitlementType AS [disabilityInformation.entitlementType]
                           								,			DisabilityInformation.sicknessStartDate AS [disabilityInformation.sicknessStartDate]
                           								,			DisabilityInformation.startLimit AS [disabilityInformation.startLimit]
                           								,			DisabilityInformation.upperLimit AS [disabilityInformation.upperLimit]
                           								,			JSON_QUERY(( employment.JSON_Bericht )) AS [employmentDatas]
                           								,			sc.migrationDate AS [migrationDate]
                           								,			sc.numberOfRetirements AS [numberOfRetirements]
                           								,			sc.participationStartDate AS [participationStartDate]
                           								,			JSON_QUERY(( partnerTypeHistory.JSON_Bericht )) AS [partnerTypeHistory]
                           								,			JSON_QUERY(( policy.JSON_Bericht )) AS [policyDatas]
                           								,			sc.relationshipCoversSplit AS [relationshipCoversSplit]
                           								,			sc.retirementDate AS [retirementDate]
                           								,			sc.retirementFraction AS retirementFraction
                           								,			sc.sleeperDate AS sleeperDate
                           								,			JSON_QUERY(( Untraceable.JSON_Bericht )) AS [untraceables]
                           								,			sc.voluntaryContributionFraction AS voluntaryContributionFraction
                           								FROM			FL.Command AS sc
                           								LEFT JOIN		JSON.Employment AS employment
                           												ON employment.DX_FK_FL_Command_ID = sc.DX_ID
                           								LEFT JOIN		JSON.Policy AS policy
                           												ON policy.DX_FK_FL_Command_ID = sc.DX_ID
                           								LEFT JOIN		JSON.PartnerTypeHistory AS partnerTypeHistory
                           												ON partnerTypeHistory.DX_FK_FL_Command_ID = sc.DX_ID
                           								LEFT JOIN		FL.DisabilityInformation AS DisabilityInformation
                           												ON DisabilityInformation.DX_FK_FL_Command_ID = sc.DX_ID
                           								LEFT JOIN		JSON.Untraceable AS Untraceable
                           												ON Untraceable.DX_FK_FL_Command_ID = sc.DX_ID
                           								WHERE			sc.DX_ID = sc_.DX_ID
                           								FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                           							)
                           			FROM	FL.Command AS sc_
                           		) jsonsel
                           """;

        var result = _analyzer.Analyze(sql, Options);
        Assert.False(result.HasErrors, $"Query should parse without errors. Errors: {string.Join("; ", result.ParseErrors)}");

        // CRITICAL: This query should have exactly 2 FinalQueryColumns as specified
        Assert.Equal(2, result.FinalQueryColumns.Count);

        // Verify the specific 2 output columns
        Assert.Contains(result.FinalQueryColumns, c => c is { TableAlias: "jsonsel", ColumnName: "DX_ID" });
        Assert.Contains(result.FinalQueryColumns, c => c.Alias == "JSON_BERICHT_Compressed");

        // Note: INSERT INTO targets may not be detected as tables by the current visitors
        // This is not a critical issue for the FinalQueryColumns functionality

        // Verify the derived table (subquery) is detected
        Assert.Contains(result.Tables, t => t is { Alias: "jsonsel", Type: TableReferenceType.DerivedTable });

        // Verify main tables from the complex nested query are detected
        Assert.Contains(result.Tables, t => t is { FullName: "FL.Command", Alias: "sc" });

        // Verify JSON-related tables are detected
        Assert.Contains(result.Tables, t => t is { FullName: "JSON.Employment", Alias: "employment" });
        Assert.Contains(result.Tables, t => t is { FullName: "JSON.Policy", Alias: "policy" });
        Assert.Contains(result.Tables, t => t is { FullName: "JSON.PartnerTypeHistory", Alias: "partnerTypeHistory" });
        Assert.Contains(result.Tables, t => t is { FullName: "JSON.Untraceable", Alias: "Untraceable" });
        Assert.Contains(result.Tables, t => t is { FullName: "FL.DisabilityInformation", Alias: "DisabilityInformation" });

        // Verify schemas from multiple levels are detected
        var schemas = result.Schemas.ToList();
        Assert.Contains("JSON", schemas);
        Assert.Contains("FL", schemas);

        // Verify we have many SelectColumns due to the nested complex query
        Assert.True(result.SelectColumns.Count > 20, $"Expected many SelectColumns from nested query, got {result.SelectColumns.Count}");

        // Verify the key distinction: FinalQueryColumns should be much less than SelectColumns
        Assert.True(result.FinalQueryColumns.Count < result.SelectColumns.Count,
            $"FinalQueryColumns ({result.FinalQueryColumns.Count}) should be less than SelectColumns ({result.SelectColumns.Count}) for this complex nested query");

        // Verify JOIN columns from the complex nested structure
        Assert.True(result.JoinColumns.Count >= 5, "Should detect multiple JOIN columns from nested query");

        // Verify some specific JOIN conditions are detected
        Assert.Contains(result.JoinColumns, c => c is { TableAlias: "employment", ColumnName: "DX_FK_FL_Command_ID" });
        Assert.Contains(result.JoinColumns, c => c is { TableAlias: "sc", ColumnName: "DX_ID" });

        // Verify function calls in the outer query
        var finalJsonColumn = result.FinalQueryColumns.FirstOrDefault(c => c.Alias == "JSON_BERICHT_Compressed");
        Assert.NotNull(finalJsonColumn);
        Assert.NotNull(finalJsonColumn.Expression);
        Assert.Contains("COMPRESS", finalJsonColumn.Expression);
    }
}
