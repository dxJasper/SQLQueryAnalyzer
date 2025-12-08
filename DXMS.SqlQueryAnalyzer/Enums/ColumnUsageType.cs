namespace DXMS.SqlQueryAnalyzer.Enums;

public enum ColumnUsageType
{
    Select,
    Where,
    Join,
    GroupBy,
    OrderBy,
    Having,
    CaseWhen,
    Function,
    Subquery
}
