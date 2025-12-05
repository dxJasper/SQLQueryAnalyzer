using SqlQueryAnalyzer;
using SqlQueryAnalyzer.Models;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// User's example query
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

Console.WriteLine(new string('=', 80));
Console.WriteLine("YOUR EXAMPLE QUERY:");
Console.WriteLine(new string('=', 80));

var analyzer = new SqlQueryAnalyzerService();
var result = analyzer.Analyze(sql, new AnalysisOptions { IncludeInnerTables = false, DeduplicateResults = true });

PrintResult(result);

// Test with GROUP BY and ORDER BY
Console.WriteLine("\n" + new string('=', 80));
Console.WriteLine("TEST WITH GROUP BY AND ORDER BY:");
Console.WriteLine(new string('=', 80));

var groupOrderQuery = """
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

var groupOrderResult = analyzer.Analyze(groupOrderQuery, new AnalysisOptions { IncludeInnerTables = false, DeduplicateResults = true });
PrintResult(groupOrderResult);

// Test with CTE
Console.WriteLine("\n" + new string('=', 80));
Console.WriteLine("TEST WITH CTE:");
Console.WriteLine(new string('=', 80));

var cteQuery = """
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

var cteResult = analyzer.Analyze(cteQuery, new AnalysisOptions { IncludeInnerTables = false, DeduplicateResults = true });
PrintResult(cteResult);

// Test with subquery
Console.WriteLine("\n" + new string('=', 80));
Console.WriteLine("TEST WITH SUBQUERY:");
Console.WriteLine(new string('=', 80));

var subqueryQuery = """
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

var subqueryResult = analyzer.Analyze(subqueryQuery, new AnalysisOptions { IncludeInnerTables = false, DeduplicateResults = true });
PrintResult(subqueryResult);

Console.ReadKey();

static void PrintResult(QueryAnalysisResult result)
{
    if (result.HasErrors)
    {
        Console.WriteLine("\n❌ PARSE ERRORS:");
        foreach (var error in result.ParseErrors)
        {
            Console.WriteLine($"   {error}");
        }
        return;
    }

    Console.WriteLine("\n✅ Query parsed successfully!\n");

    // Schemas
    var schemas = result.Schemas.ToList();
    if (schemas.Count > 0)
    {
        Console.WriteLine("📁 SCHEMAS:");
        foreach (var schema in schemas)
        {
            Console.WriteLine($"   • {schema}");
        }
    }

    // Tables
    Console.WriteLine("\n📋 TABLES:");
    foreach (var table in result.Tables)
    {
        var typeLabel = table.Type switch
        {
            TableReferenceType.Table => "TABLE",
            TableReferenceType.View => "VIEW",
            TableReferenceType.Cte => "CTE",
            TableReferenceType.DerivedTable => "DERIVED",
            TableReferenceType.TableValuedFunction => "TVF",
            _ => "UNKNOWN"
        };
        var joinLabel = table.JoinType switch
        {
            JoinType.Inner => " [INNER JOIN]",
            JoinType.Left => " [LEFT JOIN]",
            JoinType.Right => " [RIGHT JOIN]",
            JoinType.Full => " [FULL JOIN]",
            JoinType.Cross => " [CROSS JOIN]",
            _ => ""
        };
        Console.WriteLine($"   [{typeLabel}] {table.FullName}{(table.Alias is not null ? $" AS {table.Alias}" : "")}{joinLabel}");
    }

    // Select columns
    Console.WriteLine("\n📊 SELECT COLUMNS:");
    foreach (var col in result.SelectColumns)
    {
        var source = col.TableAlias ?? col.TableName ?? "[literal/expression]";
        var expr = col.Expression is not null ? $" (expr: {Truncate(col.Expression, 40)})" : "";
        Console.WriteLine($"   • {source}.{col.ColumnName}{(col.Alias is not null ? $" AS {col.Alias}" : "")}{expr}");
    }

    // Join columns
    if (result.JoinColumns.Count > 0)
    {
        Console.WriteLine("\n🔗 JOIN COLUMNS:");
        foreach (var col in result.JoinColumns)
        {
            var source = col.TableAlias ?? col.TableName ?? "[unknown]";
            Console.WriteLine($"   • {source}.{col.ColumnName}");
        }
    }

    // Predicate columns (WHERE/HAVING)
    if (result.PredicateColumns.Count > 0)
    {
        Console.WriteLine("\n🔍 WHERE/HAVING COLUMNS:");
        foreach (var col in result.PredicateColumns)
        {
            var source = col.TableAlias ?? col.TableName ?? "[unknown]";
            var usage = col.UsageType == ColumnUsageType.Having ? " [HAVING]" : " [WHERE]";
            Console.WriteLine($"   • {source}.{col.ColumnName}{usage}");
        }
    }

    // GROUP BY columns
    if (result.GroupByColumns.Count > 0)
    {
        Console.WriteLine("\n📦 GROUP BY COLUMNS:");
        foreach (var col in result.GroupByColumns)
        {
            var source = col.TableAlias ?? col.TableName ?? "[unknown]";
            Console.WriteLine($"   • {source}.{col.ColumnName}");
        }
    }

    // ORDER BY columns
    if (result.OrderByColumns.Count > 0)
    {
        Console.WriteLine("\n↕️ ORDER BY COLUMNS:");
        foreach (var col in result.OrderByColumns)
        {
            var source = col.TableAlias ?? col.TableName ?? col.Alias ?? "[unknown]";
            var direction = col.IsAscending ? "ASC" : "DESC";
            Console.WriteLine($"   • {source}.{col.ColumnName} [{direction}]");
        }
    }

    // CTEs
    if (result.CommonTableExpressions.Count > 0)
    {
        Console.WriteLine("\n📝 COMMON TABLE EXPRESSIONS (CTEs):");
        foreach (var cte in result.CommonTableExpressions)
        {
            Console.WriteLine($"   • {cte.Name}");
            if (cte.ColumnList.Count > 0)
            {
                Console.WriteLine($"     Columns: {string.Join(", ", cte.ColumnList)}");
            }
            if (cte.InnerAnalysis is not null)
            {
                Console.WriteLine($"     Inner tables: {string.Join(", ", cte.InnerAnalysis.Tables.Select(t => t.FullName))}");
            }
        }
    }

    // Subqueries
    if (result.SubQueries.Count > 0)
    {
        Console.WriteLine("\n🔄 SUBQUERIES:");
        foreach (var sq in result.SubQueries)
        {
            Console.WriteLine($"   • [{sq.Type}]{(sq.Alias is not null ? $" AS {sq.Alias}" : "")}");
            Console.WriteLine($"     Query: {Truncate(sq.QueryText.Replace("\n", " ").Replace("\r", ""), 60)}");
            if (sq.InnerAnalysis is not null)
            {
                Console.WriteLine($"     Tables: {string.Join(", ", sq.InnerAnalysis.Tables.Select(t => t.FullName))}");
            }
        }
    }

    // Column Lineage
    if (result.ColumnLineages.Count > 0)
    {
        Console.WriteLine("\n🔀 COLUMN LINEAGE:");
        foreach (var lineage in result.ColumnLineages)
        {
            var output = lineage.OutputAlias ?? lineage.OutputColumn;
            var transformLabel = lineage.Transformation switch
            {
                TransformationType.Direct => "DIRECT",
                TransformationType.Alias => "ALIAS",
                TransformationType.Cast => "CAST",
                TransformationType.Function => "FUNCTION",
                TransformationType.Aggregate => "AGGREGATE",
                TransformationType.Case => "CASE",
                TransformationType.Arithmetic => "ARITHMETIC",
                TransformationType.Literal => "LITERAL",
                TransformationType.Subquery => "SUBQUERY",
                TransformationType.WindowFunction => "WINDOW",
                _ => "UNKNOWN"
            };

            if (lineage.SourceColumns.Count > 0)
            {
                var sources = string.Join(" + ", lineage.SourceColumns.Select(s =>
                    string.IsNullOrEmpty(s.TableAlias) && string.IsNullOrEmpty(s.TableName)
                        ? s.ColumnName
                        : $"{s.TableAlias ?? s.TableName}.{s.ColumnName}"));
                Console.WriteLine($"   • {output} ← [{transformLabel}] {sources}");
            }
            else
            {
                Console.WriteLine($"   • {output} ← [{transformLabel}] (literal value)");
            }
        }
    }
}

static string Truncate(string value, int maxLength)
{
    if (string.IsNullOrEmpty(value)) return value;
    return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
}
