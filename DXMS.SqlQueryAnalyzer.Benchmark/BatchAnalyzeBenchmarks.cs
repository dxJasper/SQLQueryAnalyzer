using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.VSDiagnostics;

namespace DXMS.SqlQueryAnalyzer.Benchmark;

[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[CPUUsageDiagnoser]
public class BatchAnalyzeBenchmarks
{
    private readonly SqlQueryAnalyzerService _service = new();
    private const string BatchSimple = @"SELECT p.product_id, p.name FROM Products p; SELECT c.category_id, c.category_name FROM Categories c;";
    private const string BatchMixed = @"WITH A AS (SELECT id, name FROM T1) SELECT id FROM A; SELECT x, (SELECT MAX(y) FROM T2) AS my FROM T3; SELECT * FROM Orders o JOIN Customers cu ON o.CustomerId = cu.CustomerId;";
    private const string BatchComplex = @"DECLARE @D DATE = GETDATE(); WITH R AS (SELECT customer_id, COUNT(*) cnt FROM Orders WHERE order_date > DATEADD(month,-6,@D) GROUP BY customer_id) SELECT c.customer_id, c.name, r.cnt FROM Customers c LEFT JOIN R r ON c.customer_id = r.customer_id; SELECT p.product_id, p.name, AVG(p.price) OVER(PARTITION BY p.category_id) AS avg_cat FROM Products p WHERE p.discontinued = 0 ORDER BY avg_cat DESC;";

    [Benchmark]
    public void AnalyzeBatch_Simple()
    {
        foreach (var result in _service.AnalyzeBatch(BatchSimple))
        {
        }
    }

    [Benchmark]
    public void AnalyzeBatch_Mixed()
    {
        foreach (var result in _service.AnalyzeBatch(BatchMixed))
        {
        }
    }

    [Benchmark]
    public void AnalyzeBatch_Complex()
    {
        foreach (var result in _service.AnalyzeBatch(BatchComplex))
        {
        }
    }
}