# Runs the full LiteBus test suite with coverlet and emits a per-assembly summary.
param(
    [string]$ResultsDirectory = "./coverage",
    [switch]$UnitTestsOnly,
    [switch]$SkipReport,
    [switch]$EnforceThreshold
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$settings = if ($EnforceThreshold) { "coverlet.runsettings" } else { "coverlet.measure.runsettings" }

$testArgs = @(
    "test", "LiteBus.slnx",
    "--collect:`"XPlat Code Coverage`"",
    "--results-directory", $ResultsDirectory,
    "--settings", $settings,
    "--verbosity", "minimal"
)

if ($UnitTestsOnly) {
    $testArgs += @("--filter", "FullyQualifiedName!~IntegrationTests")
}

Write-Host "Running: dotnet $($testArgs -join ' ')"
dotnet @testArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($SkipReport) {
    exit 0
}

$coberturaFiles = Get-ChildItem -Path $ResultsDirectory -Recurse -Filter "coverage.cobertura.xml" -ErrorAction SilentlyContinue
if (-not $coberturaFiles) {
    Write-Warning "No coverage.cobertura.xml files found under $ResultsDirectory"
    exit 0
}

$reportDir = Join-Path $ResultsDirectory "report"
dotnet tool restore 2>$null
if ($LASTEXITCODE -ne 0) {
    dotnet new tool-manifest --force | Out-Null
    dotnet tool install dotnet-reportgenerator-globaltool --version 5.4.4
}

$reports = ($coberturaFiles | ForEach-Object { $_.FullName }) -join ";"
dotnet reportgenerator `
    "-reports:$reports" `
    "-targetdir:$reportDir" `
    "-reporttypes:TextSummary;HtmlSummary"

Write-Host ""
Write-Host "Coverage report: $reportDir"
Get-Content (Join-Path $reportDir "Summary.txt") -ErrorAction SilentlyContinue
