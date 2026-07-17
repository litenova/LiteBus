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

$settings = if ($EnforceThreshold) { "coverlet.runsettings" } else { "coverlet.measure.runsettings" }

$testArgs = @(
    "test", "LiteBus.slnx",
    "--collect:XPlat Code Coverage",
    "--results-directory", $ResultsDirectory,
    "--settings", $settings,
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

$allLines = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$coveredLines = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

foreach ($coverageFile in $coberturaFiles) {
    $coverageDocument = [xml](Get-Content -LiteralPath $coverageFile.FullName -Raw)
    $reportLines = @(
        foreach ($class in $coverageDocument.coverage.packages.package.classes.class) {
            foreach ($line in $class.lines.line) {
                [pscustomobject]@{
                    Key = "${class.filename}:$([string]$line.number)"
                    Hits = [int]$line.hits
                }
            }
        }
    )

    if (-not ($reportLines | Where-Object Hits -gt 0)) {
        continue
    }

    foreach ($class in $coverageDocument.coverage.packages.package.classes.class) {
        $sourceFile = [string]$class.filename

        foreach ($line in $class.lines.line) {
            $key = "${sourceFile}:$([string]$line.number)"
            $null = $allLines.Add($key)

            if ([int]$line.hits -gt 0) {
                $null = $coveredLines.Add($key)
            }
        }
    }
}

if ($allLines.Count -eq 0) {
    if ($EnforceThreshold) {
        Write-Error "Coverage reports contain no executable lines."
        exit 1
    }
}
else {
    $coveragePercent = [math]::Round(($coveredLines.Count / $allLines.Count) * 100, 2)
    Write-Host "Merged line coverage: $coveragePercent% ($($coveredLines.Count)/$($allLines.Count))"

    if ($EnforceThreshold -and $coveragePercent -lt 90) {
        Write-Error "Merged line coverage is $coveragePercent%, below the required 90% threshold."
        exit 1
    }
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
