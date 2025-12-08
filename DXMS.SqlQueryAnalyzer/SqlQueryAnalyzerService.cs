using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DXMS.SqlQueryAnalyzer;

/// <summary>
/// Analyzes T-SQL queries and extracts structural information
/// </summary>
public sealed class SqlQueryAnalyzerService : ISqlQueryAnalyzerService
{
    private readonly TSqlParser _parser = new TSql160Parser(initialQuotedIdentifiers: true);

    /// <summary>
    /// Analyzes a SQL query and returns detailed structural information
    /// </summary>
    public QueryAnalysisResult Analyze(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var fragment = Parse(sql, out var errors);

        return errors.Count > 0
            ? BuildErrorResult(sql, errors)
            : AnalyzeFragment(fragment, sql);
    }

    /// <summary>
    /// Analyzes multiple SQL statements (batch)
    /// </summary>
    public IEnumerable<QueryAnalysisResult> AnalyzeBatch(string sqlBatch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlBatch);

        var fragment = Parse(sqlBatch, out var errors);
        if (errors.Count > 0)
        {
            yield return BuildErrorResult(sqlBatch, errors);
            yield break;
        }

        if (fragment is TSqlScript script)
        {
            foreach (var batch in script.Batches)
            {
                foreach (var statement in batch.Statements)
                {
                    var statementSql = GetFragmentText(statement);
                    yield return AnalyzeFragment(statement, statementSql);
                }
            }
        }
        else
        {
            yield return AnalyzeFragment(fragment, sqlBatch);
        }
    }

    /// <summary>
    /// Validates SQL syntax without full analysis
    /// </summary>
    public (bool IsValid, List<string> Errors) ValidateSyntax(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        // We only need error list; avoid building full result
        Parse(sql, out var errors);

        return (errors.Count == 0, errors.Select(error => $"Line {error.Line}, Column {error.Column}: {error.Message}").ToList());
    }

    /// <summary>
    /// Formats a SQL query
    /// </summary>
    public string Format(string sql, SqlScriptGeneratorOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var fragment = Parse(sql, out var errors);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Cannot format invalid SQL: {errors[0].Message}");
        }

        options ??= GetDefaultGeneratorOptions();

        var generator = CreateScriptGenerator(options);
        generator.GenerateScript(fragment, out var formattedSql);
        return formattedSql;
    }

    private static SqlScriptGeneratorOptions GetDefaultGeneratorOptions() => new()
    {
        KeywordCasing = KeywordCasing.Uppercase,
        IncludeSemicolons = true,
        NewLineBeforeFromClause = true,
        NewLineBeforeWhereClause = true,
        NewLineBeforeJoinClause = true,
        NewLineBeforeGroupByClause = true,
        NewLineBeforeOrderByClause = true,
        AlignClauseBodies = true,
        IndentationSize = 4
    };

    private static Sql170ScriptGenerator CreateScriptGenerator(SqlScriptGeneratorOptions options) => new(options);

    private TSqlFragment Parse(string sql, out IList<ParseError> errors)
    {
        using var reader = new StringReader(sql);
        return _parser.Parse(reader, out errors);
    }

    private static QueryAnalysisResult BuildErrorResult(string originalSql, IList<ParseError> errors) => new()
    {
        OriginalQuery = originalSql,
        ParseErrors = errors.Select(error => $"Line {error.Line}, Column {error.Column}: {error.Message}").ToList()
    };

    private QueryAnalysisResult AnalyzeFragment(TSqlFragment fragment, string originalSql)
    {
        var result = new QueryAnalysisResult
        {
            OriginalQuery = originalSql,
            ParseErrors = []
        };

        // Extract CTEs first
        var cteVisitor = new CteVisitor(this);
        fragment.Accept(cteVisitor);
        result.CommonTableExpressions.AddRange(cteVisitor.Ctes);

        // Extract tables
        var tableVisitor = new TableReferenceVisitor();
        tableVisitor.SetCteNames(cteVisitor.Ctes.Select(c => c.Name));
        fragment.Accept(tableVisitor);
        result.Tables.AddRange(tableVisitor.Tables);

        // Extract select, predicate, join, group by, order by columns in one pass
        var compositeVisitor = new CompositeColumnVisitor();
        fragment.Accept(compositeVisitor);
        result.SelectColumns.AddRange(compositeVisitor.SelectColumns);
        result.PredicateColumns.AddRange(compositeVisitor.PredicateColumns);
        result.JoinColumns.AddRange(compositeVisitor.JoinColumns);
        result.GroupByColumns.AddRange(compositeVisitor.GroupByColumns);
        result.OrderByColumns.AddRange(compositeVisitor.OrderByColumns);

        // Extract subqueries
        var subqueryVisitor = new SubQueryVisitor(this);
        fragment.Accept(subqueryVisitor);
        result.SubQueries.AddRange(subqueryVisitor.SubQueries);

        // Extract final query columns (only the outermost SELECT that produces the actual result)
        var finalQueryVisitor = new FinalQueryColumnVisitor();
        fragment.Accept(finalQueryVisitor);
        result.FinalQueryColumns.AddRange(finalQueryVisitor.Columns);

        // Always build column lineage
        var lineageBuilder = new ColumnLineageBuilder(result.Tables);
        fragment.Accept(lineageBuilder);
        result.ColumnLineages.AddRange(lineageBuilder.Lineages);

        // Correlate columns back to tables
        var tablesByIdentifier = result.Tables
            .GroupBy(tableReference => tableReference.ReferenceIdentifier, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var reference in result.SelectColumns)
        {
            if (reference.SourceIdentifier is not null && tablesByIdentifier.TryGetValue(reference.SourceIdentifier, out var table))
            {
                table.SelectColumns.Add(reference);
            }
        }
        foreach (var reference in result.JoinColumns)
        {
            if (reference.SourceIdentifier is not null && tablesByIdentifier.TryGetValue(reference.SourceIdentifier, out var table))
            {
                table.JoinColumns.Add(reference);
            }
        }
        foreach (var reference in result.PredicateColumns)
        {
            if (reference.SourceIdentifier is not null && tablesByIdentifier.TryGetValue(reference.SourceIdentifier, out var table))
            {
                table.PredicateColumns.Add(reference);
            }
        }

        // Deduplicate per-table lists using custom comparer (refactored helper)
        DeduplicatePerTableLists(result.Tables);

        // Always deduplicate aggregate results
        RemoveDuplicates(result);

        return result;
    }

    private static void DeduplicatePerTableLists(List<QueryTableReference> tables)
    {
        foreach (var table in tables)
        {
            table.SelectColumns.Clear();
            AddUnique(table.SelectColumns, table.SelectColumns, ColumnReferenceEqualityComparer.Instance);

            table.JoinColumns.Clear();
            AddUnique(table.JoinColumns, table.JoinColumns, ColumnReferenceEqualityComparer.Instance);

            table.PredicateColumns.Clear();
            AddUnique(table.PredicateColumns, table.PredicateColumns, ColumnReferenceEqualityComparer.Instance);
        }
    }

    private static void RemoveDuplicates(QueryAnalysisResult result)
    {
        // Use custom comparers for efficient deduplication with HashSet
        var uniqueTables = new HashSet<QueryTableReference>(QueryTableReferenceEqualityComparer.Instance);
        foreach (var t in result.Tables)
        {
            uniqueTables.Add(t);
        }
        result.Tables.Clear();
        result.Tables.AddRange(uniqueTables);

        RemoveDuplicateColumns(result.SelectColumns);
        RemoveDuplicateColumns(result.PredicateColumns);
        RemoveDuplicateColumns(result.JoinColumns);
        RemoveDuplicateColumns(result.GroupByColumns);
        RemoveDuplicateColumns(result.OrderByColumns);

        var uniqueLineages = new HashSet<ColumnLineage>(ColumnLineageEqualityComparer.Instance);
        foreach (var l in result.ColumnLineages)
        {
            uniqueLineages.Add(l);
        }

        result.ColumnLineages.Clear();
        result.ColumnLineages.AddRange(uniqueLineages);
    }

    private static void RemoveDuplicateColumns(List<ColumnReference> columns)
    {
        var set = new HashSet<ColumnReference>(ColumnReferenceEqualityComparer.Instance);
        foreach (var c in columns)
        {
            set.Add(c);
        }

        columns.Clear();
        columns.AddRange(set);
    }

    private static void AddUnique<T>(List<T> target, IEnumerable<T> source, IEqualityComparer<T> comparer)
    {
        var set = new HashSet<T>(comparer);
        foreach (var item in source)
        {
            if (set.Add(item))
            {
                target.Add(item);
            }
        }
    }

    internal static string GetFragmentText(TSqlFragment fragment)
    {
        if (fragment.ScriptTokenStream is null || fragment.FirstTokenIndex < 0)
        {
            return string.Empty;
        }

        var tokens = fragment.ScriptTokenStream
            .Skip(fragment.FirstTokenIndex)
            .Take(fragment.LastTokenIndex - fragment.FirstTokenIndex + 1);

        return string.Concat(tokens.Select(t => t.Text));
    }
}
