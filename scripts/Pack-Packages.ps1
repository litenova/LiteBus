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

# Test support packages opt in with <IsPackable>true</IsPackable>; tests/Directory.Build.props defaults it to false.
$testingProjects = @(
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "tests") -Recurse -Filter "*.csproj" -File |
        Where-Object { (Get-Content -LiteralPath $_.FullName -Raw) -match "<IsPackable>\s*true\s*</IsPackable>" }
)

$projects = @(
    Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter "*.csproj" -File
    $testingProjects
) | Sort-Object FullName

if ($projects.Count -eq 0) {
    throw "No source projects were found under '$sourceRoot'."
}

# Pack in parallel: each call is mostly process start-up, so a sequential loop over about a hundred projects spends two
# minutes waiting. A project that fails in parallel (for example a transient file lock on a shared reference) is retried
# alone before the run fails.
$throttle = [Math]::Max(2, [Environment]::ProcessorCount)
$commonArguments = @("--configuration", $Configuration, "--output", $resolvedOutputDirectory)
if ($NoBuild) { $commonArguments += "--no-build" }
if ($NoRestore) { $commonArguments += "--no-restore" }
if (-not [string]::IsNullOrWhiteSpace($Version)) { $commonArguments += "/p:Version=$Version" }

$failed = @(
    $projects | ForEach-Object -ThrottleLimit $throttle -Parallel {
        $output = & dotnet pack $_.FullName @using:commonArguments 2>&1
        if ($LASTEXITCODE -ne 0) {
            [pscustomobject]@{ Project = $_; Output = ($output -join [Environment]::NewLine) }
        }
        else {
            Write-Host "Packed $($_.BaseName)"
        }
    }
)

foreach ($failure in $failed) {
    Write-Host "Retrying $($failure.Project.BaseName) alone after a parallel failure:"
    Write-Host $failure.Output
    & dotnet pack $failure.Project.FullName @commonArguments

    if ($LASTEXITCODE -ne 0) {
        throw "Packing '$($failure.Project.FullName)' failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Packed $($projects.Count) source projects into '$resolvedOutputDirectory'."
