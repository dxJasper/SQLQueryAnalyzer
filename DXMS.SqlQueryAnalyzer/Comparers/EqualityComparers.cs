namespace DXMS.SqlQueryAnalyzer.Comparers;

/// <summary>
/// Equality comparer for deduplicating ColumnReference objects
/// </summary>
public sealed class ColumnReferenceEqualityComparer : IEqualityComparer<ColumnReference>
{
    public static ColumnReferenceEqualityComparer Instance { get; } = new();

    public bool Equals(ColumnReference? x, ColumnReference? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;

        return StringComparer.OrdinalIgnoreCase.Equals(x.TableAlias, y.TableAlias) &&
               StringComparer.OrdinalIgnoreCase.Equals(x.TableName, y.TableName) &&
               StringComparer.OrdinalIgnoreCase.Equals(x.Schema, y.Schema) &&
               StringComparer.OrdinalIgnoreCase.Equals(x.ColumnName, y.ColumnName) &&
               StringComparer.OrdinalIgnoreCase.Equals(x.Alias, y.Alias) &&
               x.UsageType == y.UsageType &&
               StringComparer.OrdinalIgnoreCase.Equals(x.Expression, y.Expression) &&
               x.IsAscending == y.IsAscending;
    }

    public int GetHashCode(ColumnReference obj)
    {
        var hash = new HashCode();
        hash.Add(obj.TableAlias, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.TableName, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.Schema, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.ColumnName, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.Alias, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.UsageType);
        hash.Add(obj.Expression, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.IsAscending);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Equality comparer for deduplicating QueryTableReference objects
/// </summary>
public sealed class QueryTableReferenceEqualityComparer : IEqualityComparer<QueryTableReference>
{
    public static QueryTableReferenceEqualityComparer Instance { get; } = new();

    public bool Equals(QueryTableReference? x, QueryTableReference? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;

        return StringComparer.OrdinalIgnoreCase.Equals(x.FullName, y.FullName) &&
               StringComparer.OrdinalIgnoreCase.Equals(x.Alias, y.Alias) &&
               x.Type == y.Type &&
               x.DirectReference == y.DirectReference;
    }

    public int GetHashCode(QueryTableReference obj)
    {
        var hash = new HashCode();
        hash.Add(obj.FullName, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.Alias, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.Type);
        hash.Add(obj.DirectReference);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Equality comparer for deduplicating ColumnLineage objects
/// </summary>
public sealed class ColumnLineageEqualityComparer : IEqualityComparer<ColumnLineage>
{
    public static ColumnLineageEqualityComparer Instance { get; } = new();

    public bool Equals(ColumnLineage? x, ColumnLineage? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;

        return StringComparer.OrdinalIgnoreCase.Equals(x.OutputColumn, y.OutputColumn) &&
               StringComparer.OrdinalIgnoreCase.Equals(x.OutputAlias, y.OutputAlias) &&
               GetSourceColumnsSignature(x) == GetSourceColumnsSignature(y);
    }

    public int GetHashCode(ColumnLineage obj)
    {
        var hash = new HashCode();
        hash.Add(obj.OutputColumn, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.OutputAlias, StringComparer.OrdinalIgnoreCase);
        hash.Add(GetSourceColumnsSignature(obj));
        return hash.ToHashCode();
    }

    private static string GetSourceColumnsSignature(ColumnLineage lineage)
    {
        return string.Join("|", lineage.SourceColumns
            .Select(s => $"{s.TableAlias ?? s.TableName}:{s.ColumnName}")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
    }
}