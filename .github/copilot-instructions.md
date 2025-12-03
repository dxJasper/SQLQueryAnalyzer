# Copilot instructions for SQLQueryAnalyzer

This file gives focused, actionable guidance for AI coding agents working in this repository.

**Big Picture:**
- **Service:** `SqlQueryAnalyzerService` (in `SqlQueryAnalyzer/SqlQueryAnalyzerService.cs`) is the single high-level entry point. It parses SQL using Microsoft.SqlServer.TransactSql.ScriptDom and runs a set of small visitor classes to extract tables, columns, CTEs, subqueries and then builds column lineage.
- **Data model:** `Models/QueryAnalysisResult.cs` contains the canonical result types: `QueryAnalysisResult`, `TableReference`, `ColumnReference`, `ColumnLineage`, `CteDefinition`, and `SubQueryInfo`.
- **Visitors:** All analysis logic lives in `Visitors/*.cs`. Each visitor is named `*Visitor` and inherits from `TSqlConcreteFragmentVisitor`. They collect lists like `Columns`, `Tables`, `Ctes`, `Lineages` and are invoked by calling `fragment.Accept(visitor)` from the service.

**How to build & run (developer workflows):**
- **Build solution:** `dotnet build SqlQueryAnalyzer.slnx`
- **Run demo app:** `dotnet run --project SqlQueryAnalyzer.Demo/SqlQueryAnalyzer.Demo.csproj`
- **Format / target:** the library targets `.NET 9` (`net9.0`) and uses C# latest features (nullable, `required` properties, implicit usings).

**Concrete patterns & examples:**
- **Parsing & visitors:** See `SqlQueryAnalyzerService.Analyze` — parse once, then reuse the TSqlFragment to `Accept` multiple visitors (e.g., `TableReferenceVisitor`, `SelectColumnVisitor`, `JoinColumnVisitor`, `ColumnLineageBuilder`). Follow this pattern rather than reparsing.
- **Column extraction idiom:** `SelectColumnVisitor` shows how to handle `SelectScalarExpression` vs `SelectStarExpression`. When expressions contain inner column refs, the code collects them via an inner `ExpressionColumnVisitor` and then produces a `ColumnReference` with `Expression` populated.
- **Fragment text extraction:** Use `SqlQueryAnalyzerService.GetFragmentText(fragment)` to obtain the raw SQL text for a fragment (used for expressions, CTE query text, etc.). Prefer that helper over reconstructing tokens manually.

**Project-specific conventions:**
- **Visitor naming:** Name visitors `XxxVisitor` and keep them `internal sealed` unless they must be public.
- **Data population:** Results use immutable init-only patterns (`required` + `init`). When adding fields to models, check callers in visitors and the service for impact.
- **Error handling:** Parsing errors are returned as `ParseErrors` on `QueryAnalysisResult`. Many methods bail out early if parse errors exist — follow that approach.
- **Parser selection:** The parser is chosen by `SqlServerVersion` enum in `SqlQueryAnalyzerService.CreateParser`. Add new versions by extending that switch.

**Dependencies & integration points:**
- Primary package: `Microsoft.SqlServer.TransactSql.ScriptDom` (see `SqlQueryAnalyzer/SqlQueryAnalyzer.csproj`). Visitors operate on ScriptDom AST types.
- The `Demo` project is the main runnable example used for manual testing; prefer using it for smoke checks.

**Tests & debugging tips:**
- No unit tests present in the repo root — when adding tests, keep them focused on small visitors (unit test `SelectColumnVisitor` / `ExpressionColumnVisitor` with minimal TSQL input).
- For debugging visitors, run the demo or a small console app and print `QueryAnalysisResult` JSON. Use `SqlQueryAnalyzerService.ValidateSyntax` to quickly check parse errors.

**When changing visitors or models:**
- Update `QueryAnalysisResult` only after checking each visitor that populates the changed fields.
- If adding a new visitor, follow the existing pattern: create visitor in `Visitors/`, give it a `List<T>` property, accept it in `SqlQueryAnalyzerService.Analyze`, and append results to the corresponding list on `QueryAnalysisResult`.

If anything here is unclear or you'd like extra examples (unit tests, sample SQL cases), tell me which area to expand and I will iterate.
