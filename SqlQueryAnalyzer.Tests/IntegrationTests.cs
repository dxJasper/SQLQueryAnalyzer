using SqlQueryAnalyzer.Models;
using Xunit;

namespace SqlQueryAnalyzer.Tests;

/// <summary>
/// Integration tests based on the demo queries to ensure consistent behavior
/// These tests validate the core functionality shown in the demo
/// </summary>
public class DemoQueryTests
{
    private readonly SqlQueryAnalyzerService _analyzer = new();
    private static readonly AnalysisOptions Options = new() { IncludeInnerTables = false, DeduplicateResults = true };

    [Fact]
    public void UserExampleQuery_ComplexJoinQuery_ParsesSuccessfully()
    {
        const string sql = """
                           SELECT  
                               km_k.DX_ID AS NUMCLI, 
                               'Prospect' AS CATCLI, 
                               cast(NULL as nvarchar(50)) AS PRNCLI, 
                               rbm_p.name AS NOMCLI, 
                               CASE 
                                   WHEN rbm_ps.birth_date < CAST('1930-01-01' AS DATETIME2) 
                                   THEN CAST('1930-01-01' AS DATETIME2) 
                                   ELSE rbm_ps.birth_date 
                               END AS DATNAI,
                               rbm_p.identification_number AS NUMIDT, 
                               vrt_n.doelwaarde AS NATION, 
                               'xx' AS CODTIT, 
                               '4' AS CODLAN 
                           FROM KM.Klant AS km_k 
                           JOIN RBM.PARTIES AS rbm_p ON km_k.Party_Id = rbm_p.party_id 
                           JOIN mks.Klant_clean AS kc ON kc.identification_number = rbm_p.identification_number 
                           LEFT JOIN RBM.PERSON_SPECIFICS AS rbm_ps ON rbm_ps.party_id = rbm_p.party_id 
                           LEFT JOIN VRT.Nationaliteit AS vrt_n ON vrt_n.bronwaarde = rbm_ps.nationality_code
                           """;

        var result = _analyzer.Analyze(sql, Options);

        Assert.False(result.HasErrors);

        // For simple queries, SelectColumns and FinalQueryColumns should be equal
        Assert.Equal(9, result.SelectColumns.Count);
        Assert.Equal(9, result.FinalQueryColumns.Count);
        Assert.Equal(result.SelectColumns.Count, result.FinalQueryColumns.Count);

        // Verify we have the expected schemas
        Assert.Contains("KM", result.Schemas);
        Assert.Contains("RBM", result.Schemas);
        Assert.Contains("mks", result.Schemas);
        Assert.Contains("VRT", result.Schemas);

        // Verify we have 5 tables
        Assert.Equal(5, result.Tables.Count);

        // Verify we have JOIN columns
        Assert.True(result.JoinColumns.Count >= 6);

        // Verify column lineages
        Assert.Equal(9, result.ColumnLineages.Count);
    }

    [Fact]
    public void GroupByOrderByQuery_AggregateQuery_ParsesSuccessfully()
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
                           WHERE p.discontinued = 0
                               AND p.unit_price > 10
                           GROUP BY c.category_name, p.supplier_id
                           HAVING COUNT(*) > 5
                           ORDER BY total_value DESC, c.category_name ASC
                           """;

        var result = _analyzer.Analyze(sql, Options);

        Assert.False(result.HasErrors);

        // For simple queries, SelectColumns and FinalQueryColumns should be equal
        Assert.Equal(5, result.SelectColumns.Count);
        Assert.Equal(5, result.FinalQueryColumns.Count);
        Assert.Equal(result.SelectColumns.Count, result.FinalQueryColumns.Count);

        // Verify we have the expected schema
        Assert.Contains("dbo", result.Schemas);

        // Verify we have 3 tables
        Assert.Equal(3, result.Tables.Count);

        // Verify we have GROUP BY columns
        Assert.Equal(2, result.GroupByColumns.Count);

        // Verify we have ORDER BY columns
        Assert.Equal(2, result.OrderByColumns.Count);
    }

    [Fact]
    public void CteQuery_WithMultipleCtes_ShowsDistinctionBetweenSelectAndFinalColumns()
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

        // CRITICAL: This is the key distinction we want to maintain
        // SelectColumns includes CTE definition columns (10 total)
        Assert.Equal(10, result.SelectColumns.Count);

        // FinalQueryColumns only includes final query output (5 columns)
        Assert.Equal(5, result.FinalQueryColumns.Count);

        // FinalQueryColumns should be less than SelectColumns for CTE queries
        Assert.True(result.FinalQueryColumns.Count < result.SelectColumns.Count);

        // Verify we have CTEs
        Assert.Equal(2, result.CommonTableExpressions.Count);
        Assert.Contains(result.CommonTableExpressions, c => c.Name == "ActiveCustomers");
        Assert.Contains(result.CommonTableExpressions, c => c.Name == "RecentOrders");

        // Verify the final query columns are the expected ones
        Assert.Contains(result.FinalQueryColumns, c => c is { TableAlias: "ac", ColumnName: "customer_id" });
        Assert.Contains(result.FinalQueryColumns, c => c is { TableAlias: "ac", ColumnName: "name" });
        Assert.Contains(result.FinalQueryColumns, c => c is { TableAlias: "ac", ColumnName: "email" });
        Assert.Contains(result.FinalQueryColumns, c => c is { TableAlias: "ro", ColumnName: "order_count" });
        Assert.Contains(result.FinalQueryColumns, c => c is { TableAlias: "ro", ColumnName: "last_order" });
    }

    [Fact]
    public void SubqueryQuery_WithScalarAndExistsSubqueries_ShowsDistinctionBetweenSelectAndFinalColumns()
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

        // CRITICAL: This is the key distinction we want to maintain
        // SelectColumns includes subquery SELECT items (6 total based on actual behavior)
        Assert.Equal(6, result.SelectColumns.Count);

        // FinalQueryColumns only includes main query output (3 columns)
        Assert.Equal(3, result.FinalQueryColumns.Count);

        // FinalQueryColumns should be less than SelectColumns for subquery queries
        Assert.True(result.FinalQueryColumns.Count < result.SelectColumns.Count);

        // Verify we have subqueries
        Assert.True(result.SubQueries.Count >= 3);

        // Verify the final query columns are the expected ones
        Assert.Contains(result.FinalQueryColumns, c => c is { TableAlias: "p", ColumnName: "product_id" });
        Assert.Contains(result.FinalQueryColumns, c => c is { TableAlias: "p", ColumnName: "name" });
        Assert.Contains(result.FinalQueryColumns, c => c.Alias == "avg_price");
    }

    [Fact]
    public void AllDemoQueries_MaintainCorrectColumnCountRelationships()
    {
        // Test simple query: SelectColumns == FinalQueryColumns
        var simpleResult = _analyzer.Analyze("SELECT p.id, p.name FROM Products p");
        Assert.False(simpleResult.HasErrors);
        Assert.Equal(simpleResult.SelectColumns.Count, simpleResult.FinalQueryColumns.Count);

        // Test CTE query: FinalQueryColumns < SelectColumns
        var cteResult = _analyzer.Analyze("""
            WITH Test AS (SELECT id, name, email FROM Users)
            SELECT t.id, t.name FROM Test t
            """);
        Assert.False(cteResult.HasErrors);
        Assert.True(cteResult.FinalQueryColumns.Count < cteResult.SelectColumns.Count);

        // Test subquery: FinalQueryColumns < SelectColumns
        var subqueryResult = _analyzer.Analyze("""
            SELECT p.id, (SELECT COUNT(*) FROM Orders) as cnt
            FROM Products p
            """);
        Assert.False(subqueryResult.HasErrors);
        Assert.True(subqueryResult.FinalQueryColumns.Count < subqueryResult.SelectColumns.Count);

        // Universal rule for all queries: FinalQueryColumns should never exceed SelectColumns
        Assert.True(simpleResult.FinalQueryColumns.Count <= simpleResult.SelectColumns.Count);
        Assert.True(cteResult.FinalQueryColumns.Count <= cteResult.SelectColumns.Count);
        Assert.True(subqueryResult.FinalQueryColumns.Count <= subqueryResult.SelectColumns.Count);
    }

    [Fact]
    public void AllDemoQueries_ParseWithoutErrors()
    {
        // Smoke test to ensure all demo queries parse successfully
        var demoQueries = new[]
        {
            // User's complex query
            """
            SELECT  
                km_k.DX_ID AS NUMCLI, 
                'Prospect' AS CATCLI, 
                cast(NULL as nvarchar(50)) AS PRNCLI, 
                rbm_p.name AS NOMCLI, 
                CASE 
                    WHEN rbm_ps.birth_date < CAST('1930-01-01' AS DATETIME2) 
                    THEN CAST('1930-01-01' AS DATETIME2) 
                    ELSE rbm_ps.birth_date 
                END AS DATNAI,
                rbm_p.identification_number AS NUMIDT, 
                vrt_n.doelwaarde AS NATION, 
                'xx' AS CODTIT, 
                '4' AS CODLAN 
            FROM KM.Klant AS km_k 
            JOIN RBM.PARTIES AS rbm_p ON km_k.Party_Id = rbm_p.party_id 
            JOIN mks.Klant_clean AS kc ON kc.identification_number = rbm_p.identification_number 
            LEFT JOIN RBM.PERSON_SPECIFICS AS rbm_ps ON rbm_ps.party_id = rbm_p.party_id 
            LEFT JOIN VRT.Nationaliteit AS vrt_n ON vrt_n.bronwaarde = rbm_ps.nationality_code
            """,
            
            // GROUP BY query
            """
            SELECT 
                c.category_name,
                p.supplier_id,
                COUNT(*) AS product_count,
                SUM(p.unit_price * p.units_in_stock) AS total_value,
                AVG(p.unit_price) AS avg_price
            FROM dbo.Products p
            INNER JOIN dbo.Categories c ON p.category_id = c.category_id
            LEFT JOIN dbo.Suppliers s ON p.supplier_id = s.supplier_id
            WHERE p.discontinued = 0
                AND p.unit_price > 10
            GROUP BY c.category_name, p.supplier_id
            HAVING COUNT(*) > 5
            ORDER BY total_value DESC, c.category_name ASC
            """,
            
            // CTE query
            """
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
            """,
            
            // Subquery query
            """
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
            """,
            
            // UNPIVOT query with variable declaration
            """
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
                                    ,wezenpensioen_voorziening_initieel,wezenpensioen_voorziening_delta, pvao_voorziening
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
            """,
            
            // Complex nested JSON query
            """
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
            								,			JSON_QUERY(( partnerTypeHistory.JSON_BERicht )) AS [partnerTypeHistory]
            								,			JSON_QUERY(( policy.JSON_BERICHT )) AS [policyDatas]
            								,			sc.relationshipCoversSplit AS [relationshipCoversSplit]
            								,			sc.retirementDate AS [retirementDate]
            								,			sc.retirementFraction AS retirementFraction
            								,			sc.sleeperDate AS sleeperDate
            								,			JSON_QUERY(( Untraceable.JSON_BERICHT )) AS [untraceables]
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
            """
        };

        for (int i = 0; i < demoQueries.Length; i++)
        {
            var result = _analyzer.Analyze(demoQueries[i], Options);
            Assert.False(result.HasErrors, $"Demo query {i + 1} should parse without errors");
            Assert.True(result.FinalQueryColumns.Count > 0, $"Demo query {i + 1} should have final query columns");
            Assert.True(result.SelectColumns.Count > 0, $"Demo query {i + 1} should have select columns");
        }
    }

    [Fact]
    public void UnpivotQueryWithDeclareAndCrossApply_ReturnsExpected6FinalColumns()
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
                                                   ,wezenpensioen_voorziening_initieel,wezenpensioen_voorziening_delta, pvao_voorziening
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

        Assert.False(result.HasErrors, $"Query should parse without errors. Errors: {string.Join("; ", result.ParseErrors)}");

        // CRITICAL: This query should have exactly 6 FinalQueryColumns
        Assert.Equal(6, result.FinalQueryColumns.Count);

        // Should have 8 SelectColumns due to the DECLARE subquery
        Assert.Equal(8, result.SelectColumns.Count);

        // Verify the key distinction is maintained
        Assert.True(result.FinalQueryColumns.Count < result.SelectColumns.Count,
            "FinalQueryColumns should be less than SelectColumns for this complex query");

        // Verify the specific 6 output columns by alias/name
        var outputAliases = result.FinalQueryColumns.Select(c => c.Alias ?? c.ColumnName).ToList();
        Assert.Contains("assettype", outputAliases);
        Assert.Contains("migrate", outputAliases);
        Assert.Contains("accountType", outputAliases);
        Assert.Contains("amount", outputAliases);
        Assert.Contains("postingType", outputAliases);
        Assert.Contains("valueDate", outputAliases);
    }

    [Fact]
    public void ComplexNestedJsonQuery_Returns2FinalColumns()
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
                           								,			JSON_QUERY(( partnerTypeHistory.JSON_BERicht )) AS [partnerTypeHistory]
                           								,			JSON_QUERY(( policy.JSON_BERICHT )) AS [policyDatas]
                           								,			sc.relationshipCoversSplit AS [relationshipCoversSplit]
                           								,			sc.retirementDate AS [retirementDate]
                           								,			sc.retirementFraction AS retirementFraction
                           								,			sc.sleeperDate AS sleeperDate
                           								,			JSON_QUERY(( Untraceable.JSON_BERICHT )) AS [untraceables]
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
        // The outer SELECT has: jsonsel.DX_ID and COMPRESS(jsonsel.JSON_BERIGHT) AS JSON_BERICHT_Compressed
        // The library correctly identifies only the outermost SELECT columns as final columns
        Assert.Equal(2, result.FinalQueryColumns.Count);

        // Should have even more SelectColumns due to the deeply nested JSON query structure  
        Assert.True(result.SelectColumns.Count >= result.FinalQueryColumns.Count,
            $"SelectColumns ({result.SelectColumns.Count}) should be >= FinalQueryColumns ({result.FinalQueryColumns.Count})");

        // The main output columns should be present among the detected columns
        var outputColumns = result.FinalQueryColumns.Select(c => c.Alias ?? c.ColumnName).ToList();
        Assert.Contains("DX_ID", outputColumns);
        Assert.Contains("JSON_BERICHT_Compressed", outputColumns);

        // Verify we have tables from multiple schemas and nesting levels
        Assert.True(result.Tables.Count >= 8, $"Expected multiple tables from nested query, got {result.Tables.Count}");

        // Verify schemas are detected from multiple levels
        Assert.Contains("JSON", result.Schemas);
        Assert.Contains("FL", result.Schemas);

        // Verify derived table is detected
        Assert.Contains(result.Tables, t => t is { Alias: "jsonsel", Type: TableReferenceType.DerivedTable });

        // Verify column lineages are created (should match the number of detected columns)
        Assert.True(result.ColumnLineages.Count > 0, "Should have column lineages");
        Assert.All(result.ColumnLineages, lineage =>
            Assert.NotNull(lineage.OutputColumn));
    }
}