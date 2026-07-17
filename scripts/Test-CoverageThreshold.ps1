[CmdletBinding()]
param(
    [string]$Root = ".",
    [string]$SourceRoot = (Join-Path $PSScriptRoot "../src"),
    [Alias("Threshold")]
    [ValidateRange(1, 100)]
    [int]$LineThreshold = 90,
    [ValidateRange(0, 100)]
    [int]$BranchThreshold = 0
)

$ErrorActionPreference = "Stop"
$resolvedRoot = [IO.Path]::GetFullPath($Root)
$resolvedSourceRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($SourceRoot))
$coverageFiles = @(Get-ChildItem -LiteralPath $resolvedRoot -Recurse -Filter "coverage.json" -File)

if ($coverageFiles.Count -eq 0) {
    throw "No coverage.json files were found under '$resolvedRoot'."
}

$allLines = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$coveredLines = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$allBranches = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$coveredBranches = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$sourceDocuments = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

function Get-RelativeSourcePath {
    param([Parameter(Mandatory)][string]$DocumentPath)

    $fullDocumentPath = [IO.Path]::GetFullPath($DocumentPath)
    $relativePath = [IO.Path]::GetRelativePath($resolvedSourceRoot, $fullDocumentPath)
    $outsidePrefix = "..$([IO.Path]::DirectorySeparatorChar)"

    if ($relativePath -eq ".." -or $relativePath.StartsWith($outsidePrefix, [StringComparison]::Ordinal) -or [IO.Path]::IsPathRooted($relativePath)) {
        return $null
    }

    return $relativePath.Replace([IO.Path]::DirectorySeparatorChar, '/')
}

foreach ($coverageFile in $coverageFiles) {
    $coverageDocument = Get-Content -LiteralPath $coverageFile.FullName -Raw | ConvertFrom-Json

    foreach ($module in $coverageDocument.PSObject.Properties) {
        foreach ($document in $module.Value.PSObject.Properties) {
            $relativeSourcePath = Get-RelativeSourcePath -DocumentPath $document.Name
            if ($null -eq $relativeSourcePath) {
                continue
            }

            $null = $sourceDocuments.Add($relativeSourcePath)

            foreach ($class in $document.Value.PSObject.Properties) {
                foreach ($method in $class.Value.PSObject.Properties) {
                    foreach ($line in $method.Value.Lines.PSObject.Properties) {
                        $lineKey = "${relativeSourcePath}:$($line.Name)"
                        $null = $allLines.Add($lineKey)

                        if ([int]$line.Value -gt 0) {
                            $null = $coveredLines.Add($lineKey)
                        }
                    }

                    foreach ($branch in @($method.Value.Branches)) {
                        $branchKey = @(
                            $relativeSourcePath,
                            $class.Name,
                            $method.Name,
                            [string]$branch.Line,
                            [string]$branch.Offset,
                            [string]$branch.EndOffset,
                            [string]$branch.Path,
                            [string]$branch.Ordinal
                        ) -join '|'
                        $null = $allBranches.Add($branchKey)

                        if ([int]$branch.Hits -gt 0) {
                            $null = $coveredBranches.Add($branchKey)
                        }
                    }
                }
            }
        }
    }
}

if ($allLines.Count -eq 0) {
    throw "Coverage reports contain no executable lines under '$resolvedSourceRoot'."
}

$lineCoveragePercent = [math]::Round(($coveredLines.Count / $allLines.Count) * 100, 2)
Write-Host "Merged source coverage from $($coverageFiles.Count) reports and $($sourceDocuments.Count) documents."
Write-Host "Line coverage: $lineCoveragePercent% ($($coveredLines.Count)/$($allLines.Count))"

if (($coveredLines.Count * 100) -lt ($LineThreshold * $allLines.Count)) {
    throw "Merged line coverage is $lineCoveragePercent%, below the required $LineThreshold% threshold."
}

if ($allBranches.Count -eq 0) {
    if ($BranchThreshold -gt 0) {
        throw "Coverage reports contain no branch data under '$resolvedSourceRoot'."
    }

    Write-Host "Branch coverage: no branches reported."
    exit 0
}

$branchCoveragePercent = [math]::Round(($coveredBranches.Count / $allBranches.Count) * 100, 2)
Write-Host "Branch coverage: $branchCoveragePercent% ($($coveredBranches.Count)/$($allBranches.Count))"

if (($coveredBranches.Count * 100) -lt ($BranchThreshold * $allBranches.Count)) {
    throw "Merged branch coverage is $branchCoveragePercent%, below the required $BranchThreshold% threshold."
}
