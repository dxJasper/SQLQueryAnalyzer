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

    // Determines the ColumnKind based on the expression type.
    public static ColumnKind DetermineKind(ScalarExpression expr) => expr switch
    {
        FunctionCall func when IsAggregate(func) => ColumnKind.Aggregate,
        FunctionCall => ColumnKind.Function,
        CaseExpression or BinaryExpression or UnaryExpression => ColumnKind.Expression,
        StringLiteral or IntegerLiteral or NumericLiteral or NullLiteral => ColumnKind.Literal,
        ScalarSubquery => ColumnKind.Subquery,
        _ => ColumnKind.Expression
    };
}