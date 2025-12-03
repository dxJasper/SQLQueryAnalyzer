using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlQueryAnalyzer.Models;
using SqlQueryAnalyzer.Visitors;

namespace SqlQueryAnalyzer;

/// <summary>
/// Analyzes T-SQL queries and extracts structural information
/// </summary>
public sealed class SqlQueryAnalyzerService
{
    private readonly TSqlParser _parser;
    
    public SqlQueryAnalyzerService(SqlServerVersion version = SqlServerVersion.Sql160)
    {
        _parser = CreateParser(version);
    }
    
    /// <summary>
    /// Analyzes a SQL query and returns detailed structural information
    /// </summary>
    public QueryAnalysisResult Analyze(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        
        using var reader = new StringReader(sql);
        var fragment = _parser.Parse(reader, out var errors);
        
        var result = new QueryAnalysisResult
        {
            OriginalQuery = sql,
            ParseErrors = errors.Select(e => $"Line {e.Line}, Column {e.Column}: {e.Message}").ToList()
        };
        
        if (errors.Count > 0)
        {
            return result;
        }
        
        // Extract CTEs first
        var cteVisitor = new CteVisitor(this);
        fragment.Accept(cteVisitor);
        result.CommonTableExpressions.AddRange(cteVisitor.Ctes);
        
        // Extract tables
        var tableVisitor = new TableReferenceVisitor();
        var cteNames = cteVisitor.Ctes.Select(c => c.Name).ToList();
        tableVisitor.SetCteNames(cteNames);
        fragment.Accept(tableVisitor);
        result.Tables.AddRange(tableVisitor.Tables);
        
        // Extract select columns
        var selectColumnVisitor = new SelectColumnVisitor();
        fragment.Accept(selectColumnVisitor);
        result.SelectColumns.AddRange(selectColumnVisitor.Columns);
        
        // Extract predicate columns (WHERE)
        var predicateVisitor = new PredicateColumnVisitor();
        fragment.Accept(predicateVisitor);
        result.PredicateColumns.AddRange(predicateVisitor.Columns);
        
        // Extract join columns
        var joinVisitor = new JoinColumnVisitor();
        fragment.Accept(joinVisitor);
        result.JoinColumns.AddRange(joinVisitor.Columns);
        
        // Extract GROUP BY columns
        var groupByVisitor = new GroupByColumnVisitor();
        fragment.Accept(groupByVisitor);
        result.GroupByColumns.AddRange(groupByVisitor.Columns);
        
        // Extract ORDER BY columns
        var orderByVisitor = new OrderByColumnVisitor();
        fragment.Accept(orderByVisitor);
        result.OrderByColumns.AddRange(orderByVisitor.Columns);
        
        // Extract subqueries
        var subqueryVisitor = new SubQueryVisitor(this);
        fragment.Accept(subqueryVisitor);
        result.SubQueries.AddRange(subqueryVisitor.SubQueries);
        
        // Build column lineage
        var lineageBuilder = new ColumnLineageBuilder(result.Tables);
        fragment.Accept(lineageBuilder);
        result.ColumnLineages.AddRange(lineageBuilder.Lineages);
        
        // Remove duplicates that may have been introduced by nested queries
        RemoveDuplicates(result);
        
        return result;
    }
    
    private static void RemoveDuplicates(QueryAnalysisResult result)
    {
        // Remove duplicate tables by full name and alias combination
        var uniqueTables = result.Tables
            .GroupBy(t => new { t.FullName, t.Alias, t.Type })
            .Select(g => g.First())
            .ToList();
        
        result.Tables.Clear();
        result.Tables.AddRange(uniqueTables);
        
        // Remove duplicate columns by table, column name, alias, and usage type
        RemoveDuplicateColumns(result.SelectColumns);
        RemoveDuplicateColumns(result.PredicateColumns);
        RemoveDuplicateColumns(result.JoinColumns);
        RemoveDuplicateColumns(result.GroupByColumns);
        RemoveDuplicateColumns(result.OrderByColumns);
        
        // Remove duplicate lineages by output column name
        var uniqueLineages = result.ColumnLineages
            .GroupBy(l => new { l.OutputColumn, l.OutputAlias })
            .Select(g => g.First())
            .ToList();
        
        result.ColumnLineages.Clear();
        result.ColumnLineages.AddRange(uniqueLineages);
    }
    
    private static void RemoveDuplicateColumns(List<ColumnReference> columns)
    {
        var unique = columns
            .GroupBy(c => new { 
                c.TableAlias, 
                c.TableName, 
                c.Schema, 
                c.ColumnName, 
                c.Alias, 
                c.UsageType,
                c.Expression 
            })
            .Select(g => g.First())
            .ToList();
        
        columns.Clear();
        columns.AddRange(unique);
    }
    
    /// <summary>
    /// Analyzes multiple SQL statements (batch)
    /// </summary>
    public IEnumerable<QueryAnalysisResult> AnalyzeBatch(string sqlBatch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlBatch);
        
        using var reader = new StringReader(sqlBatch);
        var fragment = _parser.Parse(reader, out var errors);
        
        if (errors.Count > 0)
        {
            yield return new QueryAnalysisResult
            {
                OriginalQuery = sqlBatch,
                ParseErrors = errors.Select(e => $"Line {e.Line}, Column {e.Column}: {e.Message}").ToList()
            };
            yield break;
        }
        
        if (fragment is TSqlScript script)
        {
            foreach (var batch in script.Batches)
            {
                foreach (var statement in batch.Statements)
                {
                    var statementSql = GetFragmentText(statement);
                    yield return Analyze(statementSql);
                }
            }
        }
        else
        {
            yield return Analyze(sqlBatch);
        }
    }
    
    /// <summary>
    /// Validates SQL syntax without full analysis
    /// </summary>
    public (bool IsValid, List<string> Errors) ValidateSyntax(string sql)
    {
        using var reader = new StringReader(sql);
        _parser.Parse(reader, out var errors);
        
        return (errors.Count == 0, 
            errors.Select(e => $"Line {e.Line}, Column {e.Column}: {e.Message}").ToList());
    }
    
    /// <summary>
    /// Formats a SQL query
    /// </summary>
    public string Format(string sql, SqlScriptGeneratorOptions? options = null)
    {
        using var reader = new StringReader(sql);
        var fragment = _parser.Parse(reader, out var errors);
        
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Cannot format invalid SQL: {errors[0].Message}");
        }
        
        options ??= new SqlScriptGeneratorOptions
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
        
        var generator = new Sql160ScriptGenerator(options);
        generator.GenerateScript(fragment, out var formattedSql);
        
        return formattedSql;
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
    
    private static TSqlParser CreateParser(SqlServerVersion version) => version switch
    {
        SqlServerVersion.Sql100 => new TSql100Parser(initialQuotedIdentifiers: true),
        SqlServerVersion.Sql110 => new TSql110Parser(initialQuotedIdentifiers: true),
        SqlServerVersion.Sql120 => new TSql120Parser(initialQuotedIdentifiers: true),
        SqlServerVersion.Sql130 => new TSql130Parser(initialQuotedIdentifiers: true),
        SqlServerVersion.Sql140 => new TSql140Parser(initialQuotedIdentifiers: true),
        SqlServerVersion.Sql150 => new TSql150Parser(initialQuotedIdentifiers: true),
        SqlServerVersion.Sql160 => new TSql160Parser(initialQuotedIdentifiers: true),
        _ => new TSql160Parser(initialQuotedIdentifiers: true)
    };
}

public enum SqlServerVersion
{
    Sql100, // SQL Server 2008
    Sql110, // SQL Server 2012
    Sql120, // SQL Server 2014
    Sql130, // SQL Server 2016
    Sql140, // SQL Server 2017
    Sql150, // SQL Server 2019
    Sql160  // SQL Server 2022
}
