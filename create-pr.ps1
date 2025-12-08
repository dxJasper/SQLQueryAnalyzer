<#
.SYNOPSIS
    Creates a refactoring pull request for SqlQueryAnalyzer
.DESCRIPTION
    This script:
    1. Creates a new branch 'refactor/modern-csharp-patterns'
    2. Copies refactored files to appropriate locations
    3. Updates the main SqlQueryAnalyzerService to implement the interface
    4. Commits and pushes changes
    5. Creates a PR using GitHub CLI (if installed)
.NOTES
    Requires: Git, optionally GitHub CLI (gh)
    Run from: C:\Temp\Claude\SqlQueryAnalyzer
#>

$ErrorActionPreference = "Stop"

# Configuration
$repoPath = "C:\Temp\Claude\SqlQueryAnalyzer"
$branchName = "refactor/modern-csharp-patterns"
$prTitle = "Refactor: Modern C# patterns and improved architecture"
$prBody = @"
## Summary

This PR introduces modern C# patterns and architectural improvements to the SqlQueryAnalyzer library.

## Changes

### New Files
- **ISqlQueryAnalyzerService.cs** - Interface for dependency injection and testability
- **AnalysisOptions.cs** - Enhanced options with fluent builder pattern
- **ColumnReferenceFactory.cs** - Centralized column extraction logic (reduces duplication)
- **README.md** - Comprehensive documentation

### Improvements
- ✅ **Interface extraction** - `ISqlQueryAnalyzerService` enables DI and easier mocking
- ✅ **Builder pattern** - Fluent API for configuring analysis options
- ✅ **DRY principle** - `ColumnReferenceFactory` eliminates duplicate column parsing code
- ✅ **Documentation** - Full README with examples, API reference, and project structure

### New Options
- `AnalyzeNestedQueries` - Control recursive CTE/subquery analysis
- `BuildColumnLineage` - Toggle lineage building for performance
- `ForPerformance()` / `ForComprehensive()` - Preset configurations

## Usage Example

```csharp
// Dependency injection
services.AddSingleton<ISqlQueryAnalyzerService>(
    new SqlQueryAnalyzerService(SqlServerVersion.Sql160));

// Fluent options
var options = AnalysisOptions.CreateBuilder()
    .ForPerformance()
    .IncludeInnerTables(false)
    .Build();
```

## Breaking Changes
None - all changes are additive and backwards compatible.

## Testing
- [ ] Existing tests pass
- [ ] Manual verification of new features
"@

Set-Location $repoPath

Write-Host "=== SqlQueryAnalyzer Refactoring PR Script ===" -ForegroundColor Cyan
Write-Host ""

# Check if we're in the right directory
if (-not (Test-Path ".git")) {
    Write-Host "Error: Not a git repository. Please run from $repoPath" -ForegroundColor Red
    exit 1
}

# Check for uncommitted changes
$status = git status --porcelain
if ($status) {
    Write-Host "Warning: You have uncommitted changes:" -ForegroundColor Yellow
    Write-Host $status
    $continue = Read-Host "Continue anyway? (y/n)"
    if ($continue -ne 'y') {
        exit 0
    }
}

# Fetch latest and create branch
Write-Host "Fetching latest from origin..." -ForegroundColor Gray
git fetch origin

Write-Host "Creating branch: $branchName" -ForegroundColor Gray
git checkout -b $branchName 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Branch may already exist, checking it out..." -ForegroundColor Yellow
    git checkout $branchName
}

# Copy refactored files
Write-Host "Copying refactored files..." -ForegroundColor Gray

$sourceDir = "$repoPath\pr-refactoring"
$targetDir = "$repoPath\SqlQueryAnalyzer"
$visitorsDir = "$targetDir\Visitors"

# Ensure Visitors directory exists
if (-not (Test-Path $visitorsDir)) {
    New-Item -ItemType Directory -Path $visitorsDir -Force | Out-Null
}

# Copy files
Copy-Item "$sourceDir\ISqlQueryAnalyzerService.cs" "$targetDir\ISqlQueryAnalyzerService.cs" -Force
Copy-Item "$sourceDir\AnalysisOptions.cs" "$targetDir\AnalysisOptions.cs" -Force
Copy-Item "$sourceDir\ColumnReferenceFactory.cs" "$visitorsDir\ColumnReferenceFactory.cs" -Force
Copy-Item "$sourceDir\README.md" "$repoPath\README.md" -Force

Write-Host "Files copied successfully" -ForegroundColor Green

# Update SqlQueryAnalyzerService to implement interface
Write-Host "Updating SqlQueryAnalyzerService to implement interface..." -ForegroundColor Gray
$servicePath = "$targetDir\SqlQueryAnalyzerService.cs"
$serviceContent = Get-Content $servicePath -Raw

# Add interface implementation if not already present
if ($serviceContent -notmatch "ISqlQueryAnalyzerService") {
    $serviceContent = $serviceContent -replace `
        "public sealed class SqlQueryAnalyzerService", `
        "public sealed class SqlQueryAnalyzerService : ISqlQueryAnalyzerService"
    
    # Remove the duplicate AnalysisOptions class since we have a separate file now
    $serviceContent = $serviceContent -replace `
        "(?s)public sealed class AnalysisOptions\s*\{.*?public static AnalysisOptions Default.*?\}", `
        ""
    
    Set-Content $servicePath $serviceContent -NoNewline
    Write-Host "SqlQueryAnalyzerService updated" -ForegroundColor Green
}

# Stage and commit
Write-Host "Staging changes..." -ForegroundColor Gray
git add .

Write-Host "Creating commit..." -ForegroundColor Gray
git commit -m "refactor: modern C# patterns and improved architecture

- Add ISqlQueryAnalyzerService interface for DI and testability
- Add AnalysisOptions with fluent builder pattern
- Add ColumnReferenceFactory to centralize column extraction
- Add comprehensive README documentation
- New options: AnalyzeNestedQueries, BuildColumnLineage
- Preset configurations: ForPerformance(), ForComprehensive()"

# Push to origin
Write-Host "Pushing to origin..." -ForegroundColor Gray
git push -u origin $branchName

Write-Host ""
Write-Host "=== Branch pushed successfully! ===" -ForegroundColor Green
Write-Host ""

# Try to create PR with GitHub CLI
if (Get-Command gh -ErrorAction SilentlyContinue) {
    Write-Host "Creating pull request with GitHub CLI..." -ForegroundColor Gray
    
    $prBodyFile = "$env:TEMP\pr-body.md"
    $prBody | Out-File -FilePath $prBodyFile -Encoding utf8
    
    gh pr create `
        --title $prTitle `
        --body-file $prBodyFile `
        --base main `
        --head $branchName
    
    Remove-Item $prBodyFile -ErrorAction SilentlyContinue
    
    Write-Host ""
    Write-Host "=== Pull request created! ===" -ForegroundColor Green
} else {
    Write-Host "GitHub CLI (gh) not found. Create PR manually:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  https://github.com/dxJasper/SQLQueryAnalyzer/compare/main...$branchName" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Or install GitHub CLI: winget install GitHub.cli" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
