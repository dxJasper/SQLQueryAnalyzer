using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DXMS.SqlQueryAnalyzer.Utilities;

internal static class FunctionUtils
{
    private static readonly HashSet<string> Aggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM","COUNT","AVG","MIN","MAX",
        "STDEV","STDEVP","VAR","VARP",
        "COUNT_BIG","GROUPING","GROUPING_ID",
        "STRING_AGG","APPROX_COUNT_DISTINCT"
    };

    public static bool IsAggregate(FunctionCall func)
    {
        var name = func.FunctionName?.Value;
        return name is not null && Aggregates.Contains(name);
    }

    public static bool IsAggregateName(string? name)
    {
        return name is not null && Aggregates.Contains(name);
    }
}