[CmdletBinding()]
param(
    [string] $OutputDirectory = "artifacts",
    [string] $Configuration = "Release",
    [string] $Version,
    [switch] $NoBuild,
    [switch] $NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repositoryRoot "src"
$resolvedOutputDirectory = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
}
else {
    Join-Path $repositoryRoot $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $resolvedOutputDirectory | Out-Null

$projects = @(
    Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter "*.csproj" -File
    Get-Item -LiteralPath (Join-Path $repositoryRoot "tests/LiteBus.Storage.Testing/LiteBus.Storage.Testing.csproj")
) | Sort-Object FullName

if ($projects.Count -eq 0) {
    throw "No source projects were found under '$sourceRoot'."
}

foreach ($project in $projects) {
    $arguments = @(
        "pack",
        $project.FullName,
        "--configuration",
        $Configuration,
        "--output",
        $resolvedOutputDirectory
    )

    if ($NoBuild) {
        $arguments += "--no-build"
    }

    if ($NoRestore) {
        $arguments += "--no-restore"
    }

    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $arguments += "/p:Version=$Version"
    }

    Write-Host "Packing $($project.BaseName)"
    & dotnet @arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Packing '$($project.FullName)' failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Packed $($projects.Count) source projects into '$resolvedOutputDirectory'."
