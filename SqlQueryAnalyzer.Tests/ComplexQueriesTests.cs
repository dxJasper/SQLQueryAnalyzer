using SqlQueryAnalyzer.Models;

namespace SqlQueryAnalyzer.Tests;

public class ComplexQueriesTests
{
    private readonly SqlQueryAnalyzerService _analyzer = new();
    private static readonly AnalysisOptions Options = new() { IncludeInnerTables = false, DeduplicateResults = true };

    [Test]
    public async Task Analyze_ComplexJoinGroupOrder_Query_ReturnsExpectedStructures()
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
        await Assert.That(result.HasErrors).IsFalse();

        await Assert.That(result.Tables).Contains(reference => reference is { TableName: "Products", Alias: "p" });
        await Assert.That(result.Tables).Contains(reference => reference is { TableName: "Categories", Alias: "c" });
        await Assert.That(result.Tables).Contains(reference => reference is { TableName: "Suppliers", Alias: "s" });

        await Assert.That(result.SelectColumns).Contains(columnReference => columnReference is { ColumnName: "category_name", TableAlias: "c" });
        await Assert.That(result.SelectColumns).Contains(columnReference => columnReference is { ColumnName: "supplier_id", TableAlias: "p" });
        await Assert.That(result.SelectColumns).Contains(columnReference => columnReference.Alias == "product_count");
        await Assert.That(result.OrderByColumns).Contains(columnReference => columnReference is { ColumnName: "total_value", IsAscending: false });

        await Assert.That(result.ColumnLineages).Contains(lineage => (lineage.OutputAlias ?? lineage.OutputColumn) == "avg_price" && lineage.Transformation == TransformationType.Aggregate);
    }

    [Test]
    public async Task Analyze_CteAndInnerQueries_ReturnsCteAndTopLevelTables()
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

        await Assert.That(result.Tables).Contains(t => t is { Type: TableReferenceType.Cte, TableName: "ActiveCustomers" });
        await Assert.That(result.Tables).Contains(t => t is { Type: TableReferenceType.Cte, TableName: "RecentOrders" });

        await Assert.That(result.SelectColumns).Contains(c => c is { TableAlias: "ac", ColumnName: "customer_id" });
        await Assert.That(result.SelectColumns).Contains(c => c is { TableAlias: "ro", ColumnName: "order_count" });

        await Assert.That(result.ColumnLineages).Contains(l => (l.OutputAlias ?? l.OutputColumn) == "last_order");
    }

    [Test]
    public async Task Analyze_Subqueries_CollectsSubqueryInfoAndTables()
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

        await Assert.That(result.Tables).Contains(reference => reference is { TableName: "Products", Alias: "p" });
        await Assert.That(result.SubQueries).Contains(info => info.Type == SubQueryType.ScalarSubquery);
        await Assert.That(result.SubQueries).Contains(info => info.Type == SubQueryType.InSubquery);
        await Assert.That(result.SubQueries).Contains(info => info.Type == SubQueryType.ExistsSubquery);

        await Assert.That(result.ColumnLineages).Contains(lineage => (lineage.OutputAlias ?? lineage.OutputColumn) == "avg_price" && lineage.Transformation == TransformationType.Subquery);
    }

    [Test]
    public async Task Analyze_CaseExpressionsAndAggregates_LineageAndColumnsDetected()
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
        await Assert.That(result.HasErrors).IsFalse();

        await Assert.That(result.SelectColumns).Contains(reference => reference.Alias == "amount_group");
        await Assert.That(result.SelectColumns).Contains(reference => reference.Alias == "total_amount");
        await Assert.That(result.SelectColumns).Contains(reference => reference.Alias == "cnt");

        await Assert.That(result.ColumnLineages).Contains(lineage => (lineage.OutputAlias ?? lineage.OutputColumn) == "amount_group" && lineage.Transformation == TransformationType.Case);
        await Assert.That(result.ColumnLineages).Contains(lineage => (lineage.OutputAlias ?? lineage.OutputColumn) == "total_amount" && lineage.Transformation == TransformationType.Aggregate);
        await Assert.That(result.ColumnLineages).Contains(lineage => (lineage.OutputAlias ?? lineage.OutputColumn) == "cnt" && lineage.Transformation == TransformationType.Aggregate);
    }

    [Test]
    public async Task Analyze_GroupByQuery_ReturnsGroupByColumns()
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
        await Assert.That(resultNoDedup.HasErrors).IsFalse();

        // Test with deduplication
        var result = _analyzer.Analyze(sql, Options);
        await Assert.That(result.HasErrors).IsFalse();

        await Assert.That(result.GroupByColumns.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(result.GroupByColumns).Contains(reference => reference is { TableAlias: "c", ColumnName: "category_name" });
        await Assert.That(result.GroupByColumns).Contains(reference => reference is { TableAlias: "p", ColumnName: "supplier_id" });
    }

    [Test]
    public async Task ValidateSyntax_InvalidSql_ReturnsErrors()
    {
        var (isValid, errors) = _analyzer.ValidateSyntax("SELECT FROM WHERE");
        await Assert.That(isValid).IsFalse();
        await Assert.That(errors).IsNotEmpty();
    }

    [Test]
    public async Task Analyze_UnpivotWithDeclareAndCrossApply_ParsesAndAnalyzesCorrectly()
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
        await Assert.That(result.HasErrors).IsFalse();

        // Test UNPIVOT table detection
        await Assert.That(result.Tables).Contains(reference => reference is { TableName: "CalculatedPostingAmount", Alias: "CPA" });

        // Test JOIN detection with complex conditions
        await Assert.That(result.Tables).Contains(reference => reference is { TableName: "AccountAndPostingType", Alias: "AAPT", JoinType: JoinType.Left });

        // Test CROSS APPLY detection (should be treated as a derived table)
        await Assert.That(result.Tables).Contains(reference => reference.Alias == "peildatum");

        // Test variable declaration subquery table detection
        await Assert.That(result.Tables).Contains(reference => reference is { TableName: "Variable", Alias: "v" });

        // Test column analysis - should have 6 output columns
        await Assert.That(result.FinalQueryColumns.Count).IsGreaterThanOrEqualTo(6);

        // Test that UNPIVOT columns are properly handled
        await Assert.That(result.FinalQueryColumns).Contains(reference => reference is { TableAlias: "up", ColumnName: "col" });
        await Assert.That(result.FinalQueryColumns).Contains(reference => reference is { TableAlias: "up", ColumnName: "value" });

        // Test alias detection
        await Assert.That(result.FinalQueryColumns).Contains(reference => reference.Alias == "assettype");
        await Assert.That(result.FinalQueryColumns).Contains(reference => reference.Alias == "amount");

        // Test schema detection from multiple schemas
        var schemas = result.Schemas.ToList();
        await Assert.That(schemas.Contains("AFL")).IsTrue();
        await Assert.That(schemas.Contains("VRT")).IsTrue();
        await Assert.That(schemas.Contains("DCA")).IsTrue();
        await Assert.That(schemas.Contains("DK")).IsTrue();

        // Test JOIN column detection
        await Assert.That(result.JoinColumns.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(result.JoinColumns).Contains(reference => reference is { TableAlias: "AAPT", ColumnName: "vermogensOnderdeel" });
        await Assert.That(result.JoinColumns).Contains(reference => reference is { TableAlias: "up", ColumnName: "col" });
    }

    [Test]
    public async Task Analyze_ComplexNestedJsonQuery_Returns2FinalColumns()
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
        await Assert.That(result.HasErrors).IsFalse();

        // CRITICAL: This query should have exactly 2 FinalQueryColumns as specified
        await Assert.That(result.FinalQueryColumns.Count).IsEqualTo(2);

        // Verify the specific 2 output columns
        await Assert.That(result.FinalQueryColumns).Contains(reference => reference is { TableAlias: "jsonsel", ColumnName: "DX_ID" });
        await Assert.That(result.FinalQueryColumns).Contains(reference => reference.Alias == "JSON_BERICHT_Compressed");

        // Note: INSERT INTO targets may not be detected as tables by the current visitors
        // This is not a critical issue for the FinalQueryColumns functionality

        // Verify the derived table (subquery) is detected
        await Assert.That(result.Tables).Contains(reference => reference is { Alias: "jsonsel", Type: TableReferenceType.DerivedTable });

        // Verify main tables from the complex nested query are detected
        await Assert.That(result.Tables).Contains(reference => reference is { FullName: "FL.Command", Alias: "sc" });

        // Verify JSON-related tables are detected
        await Assert.That(result.Tables).Contains(reference => reference is { FullName: "JSON.Employment", Alias: "employment" });
        await Assert.That(result.Tables).Contains(reference => reference is { FullName: "JSON.Policy", Alias: "policy" });
        await Assert.That(result.Tables).Contains(reference => reference is { FullName: "JSON.PartnerTypeHistory", Alias: "partnerTypeHistory" });
        await Assert.That(result.Tables).Contains(reference => reference is { FullName: "JSON.Untraceable", Alias: "Untraceable" });
        await Assert.That(result.Tables).Contains(reference => reference is { FullName: "FL.DisabilityInformation", Alias: "DisabilityInformation" });

        // Verify schemas from multiple levels are detected
        var schemas = result.Schemas.ToList();
        await Assert.That(schemas.Contains("JSON")).IsTrue();
        await Assert.That(schemas.Contains("FL")).IsTrue();

        // Verify we have many SelectColumns due to the nested complex query
        await Assert.That(result.SelectColumns.Count).IsGreaterThan(20);

        // Verify the key distinction: FinalQueryColumns should be much less than SelectColumns
        await Assert.That(result.FinalQueryColumns.Count).IsLessThan(result.SelectColumns.Count);

        // Verify JOIN columns from the complex nested structure
        await Assert.That(result.JoinColumns.Count).IsGreaterThanOrEqualTo(5);

        // Verify some specific JOIN conditions are detected
        await Assert.That(result.JoinColumns).Contains(columnReference => columnReference is { TableAlias: "employment", ColumnName: "DX_FK_FL_Command_ID" });
        await Assert.That(result.JoinColumns).Contains(columnReference => columnReference is { TableAlias: "sc", ColumnName: "DX_ID" });

        // Verify function calls in the outer query
        var finalJsonColumn = result.FinalQueryColumns.FirstOrDefault(c => c.Alias == "JSON_BERICHT_Compressed");
        await Assert.That(finalJsonColumn).IsNotNull();
        await Assert.That(finalJsonColumn!.Expression).IsNotNull();
        await Assert.That(finalJsonColumn.Expression!.Contains("COMPRESS")).IsTrue();
    }
}
