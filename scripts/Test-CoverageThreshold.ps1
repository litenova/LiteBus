[CmdletBinding()]
param(
    [string]$Root = ".",
    [ValidateRange(1, 100)]
    [int]$Threshold = 90
)

$ErrorActionPreference = "Stop"
$resolvedRoot = [IO.Path]::GetFullPath($Root)
$coverageFiles = @(Get-ChildItem -LiteralPath $resolvedRoot -Recurse -Filter "coverage.cobertura.xml" -File)

if ($coverageFiles.Count -eq 0) {
    throw "No coverage.cobertura.xml files were found under '$resolvedRoot'."
}

$allLines = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$coveredLines = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

foreach ($coverageFile in $coverageFiles) {
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

    foreach ($reportLine in $reportLines) {
        $null = $allLines.Add($reportLine.Key)
        if ($reportLine.Hits -gt 0) {
            $null = $coveredLines.Add($reportLine.Key)
        }
    }
}

if ($allLines.Count -eq 0) {
    throw "Coverage reports contain no executable lines."
}

$coveragePercent = [math]::Round(($coveredLines.Count / $allLines.Count) * 100, 2)
Write-Host "Merged line coverage: $coveragePercent% ($($coveredLines.Count)/$($allLines.Count))"

if ($coveragePercent -lt $Threshold) {
    throw "Merged line coverage is $coveragePercent%, below the required $Threshold% threshold."
}
