[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$documentationRoot = Join-Path $repositoryRoot 'docs'
$roadmapRoot = Join-Path $repositoryRoot 'Roadmap'
$siteContentRoot = Join-Path $repositoryRoot 'site/content/docs'
$expectedSiteFiles = [System.Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
$violations = [System.Collections.Generic.List[string]]::new()

function Test-ContentMirror {
    param(
        [Parameter(Mandatory)]
        [string] $SourcePath,

        [Parameter(Mandatory)]
        [string] $SitePath
    )

    $normalizedSitePath = [IO.Path]::GetFullPath($SitePath)
    $null = $expectedSiteFiles.Add($normalizedSitePath)

    if (-not (Test-Path -LiteralPath $normalizedSitePath -PathType Leaf)) {
        $violations.Add("Missing site page for '$SourcePath': '$normalizedSitePath'.")
        return
    }

    $sourceContent = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($SourcePath))
    $siteContent = [IO.File]::ReadAllBytes($normalizedSitePath)
    $sourceHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($sourceContent))
    $siteHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($siteContent))

    if ($sourceHash -cne $siteHash) {
        $violations.Add("Site page differs from its source: '$normalizedSitePath'.")
    }
}

foreach ($sourceFile in Get-ChildItem -LiteralPath $documentationRoot -Recurse -File -Filter '*.md') {
    $relativePath = [IO.Path]::GetRelativePath($documentationRoot, $sourceFile.FullName)
    $siteRelativePath = if ($relativePath -eq 'README.md') { 'index.md' } else { $relativePath }
    Test-ContentMirror -SourcePath $sourceFile.FullName -SitePath (Join-Path $siteContentRoot $siteRelativePath)
}

foreach ($sourceFile in Get-ChildItem -LiteralPath $roadmapRoot -Recurse -File -Filter '*.md') {
    if ($sourceFile.Name -eq 'README.md') {
        continue
    }

    $relativePath = [IO.Path]::GetRelativePath($roadmapRoot, $sourceFile.FullName)
    Test-ContentMirror -SourcePath $sourceFile.FullName -SitePath (Join-Path $siteContentRoot "roadmap/$relativePath")
}

foreach ($siteFile in Get-ChildItem -LiteralPath $siteContentRoot -Recurse -File -Filter '*.md') {
    if (-not $expectedSiteFiles.Contains($siteFile.FullName)) {
        $violations.Add("Site page has no authoritative docs or roadmap source: '$($siteFile.FullName)'.")
    }
}

if ($violations.Count -gt 0) {
    $violations | Sort-Object -Unique | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    throw "Documentation site content validation failed with $($violations.Count) violation(s)."
}

Write-Host "Documentation site content validation passed for $($expectedSiteFiles.Count) mirrored pages."
