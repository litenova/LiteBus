# Runs the full LiteBus test suite with coverlet and emits a per-assembly summary.
param(
    [string]$ResultsDirectory = "./coverage",
    [switch]$UnitTestsOnly,
    [switch]$SkipReport,
    [switch]$EnforceThreshold,
    [switch]$NoBuild,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$testArgs = @(
    "test", "LiteBus.slnx",
    "--collect:XPlat Code Coverage",
    "--results-directory", $ResultsDirectory,
    "--settings", "coverlet.runsettings",
    "--configuration", $Configuration,
    "--verbosity", "minimal"
)

if ($UnitTestsOnly) {
    $testArgs += @("--filter", "FullyQualifiedName!~IntegrationTests")
}

if ($NoBuild) {
    $testArgs += @("--no-build", "--no-restore")
}

Write-Host "Running: dotnet $($testArgs -join ' ')"
dotnet @testArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$coberturaFiles = Get-ChildItem -Path $ResultsDirectory -Recurse -Filter "coverage.cobertura.xml" -ErrorAction SilentlyContinue
if (-not $coberturaFiles) {
    if ($EnforceThreshold) {
        Write-Error "No coverage.cobertura.xml files found under $ResultsDirectory."
        exit 1
    }

    Write-Warning "No coverage.cobertura.xml files found under $ResultsDirectory"
    exit 0
}

$lineThreshold = if ($EnforceThreshold) { 90 } else { 1 }
& (Join-Path $PSScriptRoot "Test-CoverageThreshold.ps1") -Root $ResultsDirectory -LineThreshold $lineThreshold
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($SkipReport) {
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
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$summaryPath = Join-Path $reportDir "Summary.txt"
if (-not (Test-Path -LiteralPath $summaryPath)) {
    Write-Error "Coverage summary was not generated at '$summaryPath'."
    exit 1
}

Write-Host ""
Write-Host "Coverage report: $reportDir"
Get-Content -LiteralPath $summaryPath
