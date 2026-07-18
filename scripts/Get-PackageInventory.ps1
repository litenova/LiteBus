[CmdletBinding()]
param(
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Filter '*.csproj' -File -Recurse
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'tests') -Filter '*.csproj' -File -Recurse
)

$evaluatedProjects = $projectFiles | ForEach-Object -Parallel {
    $projectPath = $_.FullName
    $output = & dotnet msbuild $projectPath `
        -nologo `
        '-getProperty:PackageId,IsPackable' `
        '-getItem:ProjectReference,PackageReference' 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild evaluation failed for '$projectPath': $($output -join [Environment]::NewLine)"
    }

    $evaluation = ($output -join [Environment]::NewLine) | ConvertFrom-Json
    $packageId = [string] $evaluation.Properties.PackageId

    if ([string]::IsNullOrWhiteSpace($packageId)) {
        $packageId = [IO.Path]::GetFileNameWithoutExtension($projectPath)
    }

    $projectReferences = @($evaluation.Items.ProjectReference | ForEach-Object {
        [pscustomobject]@{
            FullPath = [string] $_.FullPath
            FallbackName = [string] $_.Filename
        }
    })

    $packageReferences = @($evaluation.Items.PackageReference | Where-Object {
        [string]::Equals(
            [IO.Path]::GetFullPath([string] $_.DefiningProjectFullPath),
            [IO.Path]::GetFullPath($projectPath),
            [StringComparison]::OrdinalIgnoreCase)
    } | ForEach-Object {
        [pscustomobject]@{
            Name = [string] $_.Identity
            PrivateAssets = [string] $_.PrivateAssets
        }
    })

    [pscustomobject]@{
        ProjectPath = [IO.Path]::GetFullPath($projectPath)
        PackageId = $packageId
        IsPackable = [string] $evaluation.Properties.IsPackable
        ProjectReferences = $projectReferences
        PackageReferences = $packageReferences
    }
} -ThrottleLimit ([Math]::Min([Environment]::ProcessorCount, 8))

$shippingProjects = @($evaluatedProjects | Where-Object {
    -not [string]::Equals($_.IsPackable, 'false', [StringComparison]::OrdinalIgnoreCase)
})
$packageIdByProjectPath = @{}

foreach ($project in $shippingProjects) {
    $packageIdByProjectPath[$project.ProjectPath] = $project.PackageId
}

function Format-CodeList {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]] $Values
    )

    if ($Values.Count -eq 0) {
        return 'none'
    }

    return ($Values | Sort-Object -Unique | ForEach-Object { "``$_``" }) -join ', '
}

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Generated Package Inventory')
$lines.Add('')
$lines.Add('This file is generated from evaluated MSBuild metadata by `scripts/Get-PackageInventory.ps1`. Do not edit the table by hand.')
$lines.Add('')
$lines.Add('| Package | Direct project references | Direct package references |')
$lines.Add('| --- | --- | --- |')

foreach ($project in $shippingProjects | Sort-Object PackageId) {
    $projectReferenceNames = @($project.ProjectReferences | ForEach-Object {
        $referencePath = [IO.Path]::GetFullPath($_.FullPath)

        if ($packageIdByProjectPath.ContainsKey($referencePath)) {
            $packageIdByProjectPath[$referencePath]
        }
        else {
            $_.FallbackName
        }
    })
    $packageReferenceNames = @($project.PackageReferences | ForEach-Object {
        if ([string]::IsNullOrWhiteSpace($_.PrivateAssets)) {
            $_.Name
        }
        else {
            "$($_.Name) (private)"
        }
    })

    $lines.Add(
        "| ``$($project.PackageId)`` | $(Format-CodeList -Values $projectReferenceNames) | " +
        "$(Format-CodeList -Values $packageReferenceNames) |")
}

$content = [string]::Join("`n", $lines) + "`n"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Write-Output $content -NoEnumerate
    return
}

$resolvedOutputPath = if ([IO.Path]::IsPathRooted($OutputPath)) {
    [IO.Path]::GetFullPath($OutputPath)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
}

[IO.File]::WriteAllText($resolvedOutputPath, $content, [Text.UTF8Encoding]::new($false))
Write-Host "Wrote evaluated package inventory to '$resolvedOutputPath'."
