[CmdletBinding()]
param(
    # Fail instead of reporting. Off by default: a snapshot falls behind when a fix lands on the branch it tracks,
    # which is work on another branch, and blocking this one for it would stop unrelated changes.
    [switch] $Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$versionsPath = Join-Path $repositoryRoot 'site/versions.json'

if (-not (Test-Path -LiteralPath $versionsPath -PathType Leaf)) {
    throw "Documentation versions file not found at '$versionsPath'."
}

$versions = (Get-Content -LiteralPath $versionsPath -Raw | ConvertFrom-Json).versions
$tracked = @($versions | Where-Object { $_.PSObject.Properties.Name -contains 'tracks' -and $_.tracks })

if ($tracked.Count -eq 0) {
    Write-Host "No documentation version declares a tracked branch. Every snapshot is final."
    exit 0
}

$driftFound = $false

foreach ($version in $tracked) {
    $snapshotPath = Join-Path $repositoryRoot "site/$($version.dir)"

    if (-not (Test-Path -LiteralPath $snapshotPath -PathType Container)) {
        # The branch being checked does not carry this snapshot. Nothing to compare it against.
        Write-Host "Version '$($version.id)': no snapshot at 'site/$($version.dir)' on this branch. Skipped."
        continue
    }

    $branch = $version.tracks
    $sourcePath = "site/content/docs"

    # The tracked branch holds the line's working documentation, which is what the snapshot was cut from.
    & git rev-parse --verify --quiet "origin/$branch^{commit}" | Out-Null

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Version '$($version.id)': 'origin/$branch' is not available in this clone. Skipped."
        continue
    }

    $comparison = & git diff --name-status "origin/${branch}:${sourcePath}" "HEAD:site/$($version.dir)" 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "Comparing 'origin/${branch}:${sourcePath}' against 'site/$($version.dir)' failed: $comparison"
    }

    $changes = @($comparison | Where-Object { $_ })

    if ($changes.Count -eq 0) {
        Write-Host "Version '$($version.id)': snapshot matches 'origin/$branch'."
        continue
    }

    $driftFound = $true

    Write-Host ""
    Write-Host "Version '$($version.id)': snapshot at 'site/$($version.dir)' differs from 'origin/$branch':"

    foreach ($change in $changes) {
        Write-Host "  $change"
    }

    Write-Host ""
    Write-Host "  The '$($version.id)' line is still maintained on '$branch', so a documentation fix lands there first."
    Write-Host "  Bring the snapshot back in step, from the repository root:"
    Write-Host ""
    Write-Host "    git checkout origin/$branch -- $sourcePath"
    Write-Host "    git mv -f $sourcePath/<file> site/$($version.dir)/<file>   # per corrected file"
    Write-Host ""
    Write-Host "  Or drop 'tracks' from the '$($version.id)' entry in site/versions.json once the line is final."
}

if (-not $driftFound) {
    Write-Host ""
    Write-Host "Every tracked documentation snapshot is in step with the branch it tracks."
    exit 0
}

if ($Strict) {
    Write-Error "A documentation snapshot has fallen behind the branch it tracks."
    exit 1
}

exit 0
