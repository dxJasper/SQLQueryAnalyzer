using SqlQueryAnalyzer.Models;

namespace SqlQueryAnalyzer.Tests;

/// <summary>
/// Integration tests using real-world complex SQL queries to validate end-to-end functionality
/// These tests ensure that the SqlQueryAnalyzer correctly handles various SQL patterns
/// and maintains consistent behavior across different query types
/// </summary>
public class IntegrationTests
{
    private readonly SqlQueryAnalyzerService _analyzer = new();
    private static readonly AnalysisOptions Options = new() { IncludeInnerTables = false, DeduplicateResults = true };

    [Test]
    public async Task UserExampleQuery_ComplexJoinQuery_ParsesSuccessfully()
    {
        var sql = """
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
        await Assert.That(result.HasErrors).IsFalse();

        // For simple queries, SelectColumns and FinalQueryColumns should be equal
        await Assert.That(result.SelectColumns.Count).IsEqualTo(9);
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(9);
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(result.SelectColumns.Count);

        // Verify we have the expected schemas
        await Assert.That(result.Schemas.Contains("KM")).IsTrue();
        await Assert.That(result.Schemas.Contains("RBM")).IsTrue();
        await Assert.That(result.Schemas.Contains("mks")).IsTrue();
        await Assert.That(result.Schemas.Contains("VRT")).IsTrue();

        // Verify we have 5 tables
        await Assert.That(result.Tables.Count).IsEqualTo(5);

        // Verify we have JOIN columns
        await Assert.That(result.JoinColumns.Count).IsGreaterThanOrEqualTo(6);

        // Verify column lineages
        await Assert.That(result.ColumnLineages.Count).IsEqualTo(9);
    }

    [Test]
    public async Task GroupByOrderByQuery_AggregateQuery_ParsesSuccessfully()
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
            WHERE p.discontinued = 0
                AND p.unit_price > 10
            GROUP BY c.category_name, p.supplier_id
            HAVING COUNT(*) > 5
            ORDER BY total_value DESC, c.category_name ASC
            """;

        var result = _analyzer.Analyze(sql, Options);
        await Assert.That(result.HasErrors).IsFalse();

        // For simple queries, SelectColumns and FinalQueryColumns should be equal
        await Assert.That(result.SelectColumns.Count).IsEqualTo(5);
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(5);
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(result.SelectColumns.Count);

        // Verify we have the expected schema
        await Assert.That(result.Schemas.Contains("dbo")).IsTrue();

        // Verify we have 3 tables
        await Assert.That(result.Tables.Count).IsEqualTo(3);

        // Verify we have GROUP BY columns
        await Assert.That(result.GroupByColumns.Count).IsEqualTo(2);

        // Verify we have ORDER BY columns
        await Assert.That(result.OrderByColumns.Count).IsEqualTo(2);
    }

    [Test]
    public async Task CteQuery_WithMultipleCtes_ShowsDistinctionBetweenSelectAndFinalColumns()
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
        await Assert.That(result.HasErrors).IsFalse();

        // CRITICAL: This is the key distinction we want to maintain
        // SelectColumns includes CTE definition columns (10 total)
        await Assert.That(result.SelectColumns.Count).IsEqualTo(10);

        // FinalQueryColumns only includes final query output (5 columns)
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(5);

        // FinalQueryColumns should be less than SelectColumns for CTE queries
        await Assert.That(result.FinalQueryColumns.Count).IsLessThan(result.SelectColumns.Count);

        // Verify we have CTEs
        await Assert.That(result.CommonTableExpressions.Count).IsEqualTo(2);
        await Assert.That(result.CommonTableExpressions.Any(c => c.Name == "ActiveCustomers")).IsTrue();
        await Assert.That(result.CommonTableExpressions.Any(c => c.Name == "RecentOrders")).IsTrue();

        // Verify the final query columns are the expected ones
        await Assert.That(result.FinalQueryColumns.Any(c => c.TableAlias == "ac" && c.ColumnName == "customer_id")).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c.TableAlias == "ac" && c.ColumnName == "name")).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c.TableAlias == "ac" && c.ColumnName == "email")).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c.TableAlias == "ro" && c.ColumnName == "order_count")).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c.TableAlias == "ro" && c.ColumnName == "last_order")).IsTrue();
    }

    [Test]
    public async Task SubqueryQuery_WithScalarAndExistsSubqueries_ShowsDistinctionBetweenSelectAndFinalColumns()
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
        await Assert.That(result.HasErrors).IsFalse();

        // CRITICAL: This is the key distinction we want to maintain
        // SelectColumns includes subquery SELECT items (6 total based on actual behavior)
        await Assert.That(result.SelectColumns.Count).IsEqualTo(6);

        // FinalQueryColumns only includes main query output (3 columns)
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(3);

        // FinalQueryColumns should be less than SelectColumns for subquery queries
        await Assert.That(result.FinalQueryColumns.Count).IsLessThan(result.SelectColumns.Count);

        // Verify we have subqueries
        await Assert.That(result.SubQueries.Count).IsGreaterThanOrEqualTo(3);

        // Verify the final query columns are the expected ones
        await Assert.That(result.FinalQueryColumns.Any(c => c.TableAlias == "p" && c.ColumnName == "product_id")).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c.TableAlias == "p" && c.ColumnName == "name")).IsTrue();
        await Assert.That(result.FinalQueryColumns.Any(c => c.Alias == "avg_price")).IsTrue();
    }

    [Test]
    public async Task AllDemoQueries_MaintainCorrectColumnCountRelationships()
    {
        // Test simple query: SelectColumns == FinalQueryColumns
        var simpleResult = _analyzer.Analyze("SELECT p.id, p.name FROM Products p");
        await Assert.That(simpleResult.HasErrors).IsFalse();
        await Assert.That(simpleResult.FinalQueryColumns.Count).IsEqualTo(simpleResult.SelectColumns.Count);

        // Test CTE query: FinalQueryColumns < SelectColumns
        var cteResult = _analyzer.Analyze("""
            WITH Test AS (SELECT id, name, email FROM Users)
            SELECT t.id, t.name FROM Test t
            """);
        await Assert.That(cteResult.HasErrors).IsFalse();
        await Assert.That(cteResult.FinalQueryColumns.Count).IsLessThan(cteResult.SelectColumns.Count);

        // Test subquery: FinalQueryColumns < SelectColumns
        var subqueryResult = _analyzer.Analyze("""
            SELECT p.id, (SELECT COUNT(*) FROM Orders) as cnt
            FROM Products p
            """);
        await Assert.That(subqueryResult.HasErrors).IsFalse();
        await Assert.That(subqueryResult.FinalQueryColumns.Count).IsLessThan(subqueryResult.SelectColumns.Count);

        // Universal rule for all queries: FinalQueryColumns should never exceed SelectColumns
        await Assert.That(simpleResult.FinalQueryColumns.Count).IsLessThanOrEqualTo(simpleResult.SelectColumns.Count);
        await Assert.That(cteResult.FinalQueryColumns.Count).IsLessThanOrEqualTo(cteResult.SelectColumns.Count);
        await Assert.That(subqueryResult.FinalQueryColumns.Count).IsLessThanOrEqualTo(subqueryResult.SelectColumns.Count);
    }

    [Test]
    public async Task AllDemoQueries_ParseWithoutErrors()
    {
        // Smoke test to ensure all demo queries parse successfully
        var demoQueries = new[]
        {
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
            await Assert.That(result.HasErrors).IsFalse();
            await Assert.That(result.FinalQueryColumns.Count).IsGreaterThan(0);
            await Assert.That(result.SelectColumns.Count).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task UnpivotQueryWithDeclareAndCrossApply_ReturnsExpected6FinalColumns()
    {
        var sql = """
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
        await Assert.That(result.HasErrors).IsFalse();

        // CRITICAL: This query should have exactly 6 FinalQueryColumns
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(6);

        // Should have 8 SelectColumns due to the DECLARE subquery
        await Assert.That(result.SelectColumns.Count).IsEqualTo(8);

        // Verify the key distinction is maintained
        await Assert.That(result.FinalQueryColumns.Count).IsLessThan(result.SelectColumns.Count);

        // Verify the specific 6 output columns by alias/name
        var outputAliases = result.FinalQueryColumns.Select(c => c.Alias ?? c.ColumnName).ToList();
        await Assert.That(outputAliases.Contains("assettype")).IsTrue();
        await Assert.That(outputAliases.Contains("migrate")).IsTrue();
        await Assert.That(outputAliases.Contains("accountType")).IsTrue();
        await Assert.That(outputAliases.Contains("amount")).IsTrue();
        await Assert.That(outputAliases.Contains("postingType")).IsTrue();
        await Assert.That(outputAliases.Contains("valueDate")).IsTrue();
    }

    [Test]
    public async Task ComplexNestedJsonQuery_Returns2FinalColumns()
    {
        var sql = """
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
            								,			DisabilityInformation.disabilityClassType AS [disabilityInformation.disabilityClassType]
            								,			DisabilityInformation.disabilityFraction AS [disabilityInformation.disabilityFraction]
            								,			DisabilityInformation.disabilityType AS [disabilityInformation.disabilityType]
            								,			DisabilityInformation.entitlementEndDate AS [disabilityInformation.entitlementEndDate]
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
        await Assert.That(result.HasErrors).IsFalse();

        // CRITICAL: This query should have exactly 2 FinalQueryColumns as expected
        // The FinalQueryColumnVisitor correctly identifies only the outer SELECT columns
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(2);
        
        // Should have even more SelectColumns due to the deeply nested JSON query structure  
        await Assert.That(result.SelectColumns.Count).IsGreaterThanOrEqualTo(result.FinalQueryColumns.Count);
        
        // The main output columns should be present among the detected columns
        var outputColumns = result.FinalQueryColumns.Select(c => c.Alias ?? c.ColumnName).ToList();
        await Assert.That(outputColumns.Contains("DX_ID")).IsTrue();
        await Assert.That(outputColumns.Contains("JSON_BERICHT_Compressed")).IsTrue();

        // Verify we have tables from multiple schemas and nesting levels
        await Assert.That(result.Tables.Count).IsGreaterThanOrEqualTo(8);
        
        // Verify schemas are detected from multiple levels
        await Assert.That(result.Schemas.Contains("JSON")).IsTrue();
        await Assert.That(result.Schemas.Contains("FL")).IsTrue();

        // Verify derived table is detected
        await Assert.That(result.Tables.Any(t => t.Alias == "jsonsel" && t.Type == TableReferenceType.DerivedTable)).IsTrue();

        // Verify column lineages are created (should match the number of detected columns)
        await Assert.That(result.ColumnLineages.Count).IsGreaterThan(0);
    }
}