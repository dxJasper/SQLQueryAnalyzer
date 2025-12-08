using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.VSDiagnostics;

namespace DXMS.SqlQueryAnalyzer.Benchmark;

[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[CPUUsageDiagnoser]
public class AnalyzeBenchmarks
{
    private readonly SqlQueryAnalyzerService _service = new();
    private const string Simple = "SELECT p.product_id, p.name, p.price FROM Products p";
    private const string Aliases = "SELECT p.product_id AS id, p.name AS product_name, 'Active' AS status FROM Products p";
    private const string Cte = @"WITH ActiveCustomers AS (SELECT customer_id, name, email, phone FROM dbo.Customers WHERE status = 'Active') SELECT ac.customer_id, ac.name FROM ActiveCustomers ac";
    private const string MultiCte = @"WITH ActiveCustomers AS (SELECT customer_id, name, email FROM dbo.Customers WHERE status = 'Active'), RecentOrders AS (SELECT customer_id, COUNT(*) as order_count, MAX(order_date) as last_order FROM dbo.Orders WHERE order_date > DATEADD(month, -3, GETDATE()) GROUP BY customer_id) SELECT ac.customer_id, ac.name, ro.order_count FROM ActiveCustomers ac LEFT JOIN RecentOrders ro ON ac.customer_id = ro.customer_id";
    private const string SubquerySelect = @"SELECT p.product_id, p.name, (SELECT AVG(price) FROM dbo.Products) as avg_price FROM dbo.Products p";
    private const string Complex = @"SELECT c.category_name, p.supplier_id, COUNT(*) AS product_count, SUM(p.unit_price * p.units_in_stock) AS total_value, AVG(p.unit_price) AS avg_price FROM dbo.Products p INNER JOIN dbo.Categories c ON p.category_id = c.category_id WHERE p.discontinued = 0 GROUP BY c.category_name, p.supplier_id HAVING COUNT(*) > 5 ORDER BY total_value DESC";
    private const string Unpivot = @"DECLARE @MOGID AS NVARCHAR(255) = (SELECT v.Value FROM DCA.Variable AS v WHERE v.Name = 'MOGID') SELECT up.col AS assettype, AAPT.migrate AS migrate, AAPT.accountType AS accountType, up.value AS amount, AAPT.postingType AS postingType, peildatum.valueDate FROM AFL.CalculatedPostingAmount CPA UNPIVOT ( value FOR col IN ( budget_inhaalindexatie, budget_standaardregel, budget_aanvulling_tv , budget_compensatiedepot, solidariteitsreserve, solidariteitsreserve_initieel , solidariteitsreserve_delta, operationele_reserve, kostenvoorziening , kostenvoorziening_initieel, kostenvoorziening_delta, wezenpensioen_voorziening , wezenpensioen_voorziening_initieel, wezenpensioen_voorziening_delta, pvao_voorziening , pvao_voorziening_initieel, pvao_voorziening_delta, ibnr_aop_voorziening , ibnr_aop_voorziening_initieel, ibnr_aop_voorziening_delta, ibnr_pvao_voorziening , ibnr_pvao_voorziening_initieel, ibnr_pvao_voorziening_delta, totaal_fondsvermogen , totaal_fondsvermogen_initieel, totaal_fondsvermogen_delta ) ) up LEFT JOIN VRT.AccountAndPostingType AAPT ON AAPT.vermogensOnderdeel = up.col AND AAPT.MOGID = @MOGID CROSS APPLY ( SELECT MAX(lvpkc.PEILDATUMFUNC) AS valueDate FROM DK.L33_V_PVS_KLANT_CONTACTPUNT AS lvpkc ) AS peildatum";

    [Benchmark]
    public void Analyze_Simple() => _service.Analyze(Simple);
    [Benchmark]
    public void Analyze_Aliases() => _service.Analyze(Aliases);
    [Benchmark]
    public void Analyze_Cte() => _service.Analyze(Cte);
    [Benchmark]
    public void Analyze_MultiCte() => _service.Analyze(MultiCte);
    [Benchmark]
    public void Analyze_SubquerySelect() => _service.Analyze(SubquerySelect);
    [Benchmark]
    public void Analyze_Complex() => _service.Analyze(Complex);
    [Benchmark]
    public void Analyze_Unpivot() => _service.Analyze(Unpivot);
}