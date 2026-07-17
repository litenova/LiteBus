[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory,

    [string] $ExpectedVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression.FileSystem

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repositoryRoot "src"
$testingProjects = @(
    "tests/LiteBus.Storage.Testing/LiteBus.Storage.Testing.csproj"
    "tests/LiteBus.Transport.Testing/LiteBus.Transport.Testing.csproj"
) | ForEach-Object { Get-Item -LiteralPath (Join-Path $repositoryRoot $_) }
$resolvedPackageDirectory = if ([IO.Path]::IsPathRooted($PackageDirectory)) {
    $PackageDirectory
}
else {
    Join-Path $repositoryRoot $PackageDirectory
}

if (-not (Test-Path -LiteralPath $resolvedPackageDirectory -PathType Container)) {
    throw "Package directory '$resolvedPackageDirectory' does not exist."
}

$errors = [Collections.Generic.List[string]]::new()
$expectedPackageIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$projectFiles = Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter "*.csproj" -File
$projectFiles = @($projectFiles + $testingProjects)

foreach ($projectFile in $projectFiles) {
    [xml] $project = Get-Content -LiteralPath $projectFile.FullName -Raw
    $isPackableValue = @($project.SelectNodes("/Project/PropertyGroup/IsPackable")) |
        ForEach-Object InnerText |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Last 1

    if ($isPackableValue -and [string]::Equals($isPackableValue, "false", [StringComparison]::OrdinalIgnoreCase)) {
        continue
    }

    $packageIdValue = @($project.SelectNodes("/Project/PropertyGroup/PackageId")) |
        ForEach-Object InnerText |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Last 1
    $packageId = if ($packageIdValue) { [string] $packageIdValue } else { $projectFile.BaseName }

    if (-not $expectedPackageIds.Add($packageId)) {
        $errors.Add("Source projects declare duplicate package ID '$packageId'.")
    }
}

$packageFiles = @(Get-ChildItem -LiteralPath $resolvedPackageDirectory -Filter "*.nupkg" -File)
$symbolPackageFiles = @(Get-ChildItem -LiteralPath $resolvedPackageDirectory -Filter "*.snupkg" -File)
$actualPackageIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$packageVersions = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$packageMetadata = [Collections.Generic.List[object]]::new()

foreach ($packageFile in $packageFiles) {
    $archive = [IO.Compression.ZipFile]::OpenRead($packageFile.FullName)

    try {
        $entries = @($archive.Entries | ForEach-Object FullName)
        $nuspecEntries = @($archive.Entries | Where-Object FullName -Like "*.nuspec")

        if ($nuspecEntries.Count -ne 1) {
            $errors.Add("Package '$($packageFile.Name)' contains $($nuspecEntries.Count) nuspec files; expected one.")
            continue
        }

        $reader = [IO.StreamReader]::new($nuspecEntries[0].Open())

        try {
            [xml] $nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $namespaceManager = [Xml.XmlNamespaceManager]::new($nuspec.NameTable)
        $namespaceManager.AddNamespace("n", $nuspec.DocumentElement.NamespaceURI)
        $metadata = $nuspec.SelectSingleNode("/n:package/n:metadata", $namespaceManager)
        $packageId = $metadata.SelectSingleNode("n:id", $namespaceManager).InnerText
        $packageVersion = $metadata.SelectSingleNode("n:version", $namespaceManager).InnerText

        if (-not $actualPackageIds.Add($packageId)) {
            $errors.Add("Package directory contains duplicate package ID '$packageId'.")
        }

        [void] $packageVersions.Add($packageVersion)
        $packageMetadata.Add([pscustomobject]@{
            Id = $packageId
            Version = $packageVersion
            Metadata = $metadata
            NamespaceManager = $namespaceManager
            Entries = $entries
            FileName = $packageFile.Name
        })

        foreach ($requiredEntry in @("README.md", "icon.png")) {
            if ($entries -notcontains $requiredEntry) {
                $errors.Add("Package '$packageId' does not contain '$requiredEntry'.")
            }
        }

        if ($packageId -eq "LiteBus.Analyzers") {
            if ($entries -notcontains "analyzers/dotnet/cs/LiteBus.Analyzers.dll") {
                $errors.Add("Analyzer package does not contain its Roslyn analyzer assembly.")
            }

            if ($entries | Where-Object { $_ -like "lib/*" }) {
                $errors.Add("Analyzer package must not expose a runtime lib asset.")
            }
        }
        else {
            foreach ($requiredEntry in @(
                "lib/net10.0/$packageId.dll",
                "lib/net10.0/$packageId.xml"
            )) {
                if ($entries -notcontains $requiredEntry) {
                    $errors.Add("Package '$packageId' does not contain '$requiredEntry'.")
                }
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

foreach ($expectedPackageId in $expectedPackageIds) {
    if (-not $actualPackageIds.Contains($expectedPackageId)) {
        $errors.Add("Expected package '$expectedPackageId' was not produced.")
    }
}

foreach ($actualPackageId in $actualPackageIds) {
    if (-not $expectedPackageIds.Contains($actualPackageId)) {
        $errors.Add("Unexpected package '$actualPackageId' was produced.")
    }
}

if ($actualPackageIds.Contains("LiteBus.Extensions.All")) {
    $errors.Add("Removed package 'LiteBus.Extensions.All' must not be produced.")
}

if ($packageVersions.Count -ne 1) {
    $errors.Add("Package directory contains multiple versions: $([string]::Join(', ', $packageVersions)).")
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and -not $packageVersions.Contains($ExpectedVersion)) {
    $errors.Add("Packages do not use expected version '$ExpectedVersion'.")
}

foreach ($package in $packageMetadata) {
    $authors = $package.Metadata.SelectSingleNode("n:authors", $package.NamespaceManager)
    $description = $package.Metadata.SelectSingleNode("n:description", $package.NamespaceManager)
    $license = $package.Metadata.SelectSingleNode("n:license", $package.NamespaceManager)
    $projectUrl = $package.Metadata.SelectSingleNode("n:projectUrl", $package.NamespaceManager)
    $readme = $package.Metadata.SelectSingleNode("n:readme", $package.NamespaceManager)
    $icon = $package.Metadata.SelectSingleNode("n:icon", $package.NamespaceManager)
    $repository = $package.Metadata.SelectSingleNode("n:repository", $package.NamespaceManager)
    $tags = $package.Metadata.SelectSingleNode("n:tags", $package.NamespaceManager)

    if ($null -eq $authors -or [string]::IsNullOrWhiteSpace($authors.InnerText)) {
        $errors.Add("Package '$($package.Id)' does not declare authors.")
    }

    if ($null -eq $description -or [string]::IsNullOrWhiteSpace($description.InnerText)) {
        $errors.Add("Package '$($package.Id)' does not declare a description.")
    }

    if ($null -eq $license -or $license.InnerText -ne "MIT") {
        $errors.Add("Package '$($package.Id)' does not declare the MIT license expression.")
    }

    if ($null -eq $projectUrl -or $projectUrl.InnerText -ne "https://github.com/litenova/LiteBus") {
        $errors.Add("Package '$($package.Id)' does not declare the canonical project URL.")
    }

    if ($null -eq $readme -or $readme.InnerText -ne "README.md") {
        $errors.Add("Package '$($package.Id)' does not declare README.md as its package readme.")
    }

    if ($null -eq $icon -or $icon.InnerText -ne "icon.png") {
        $errors.Add("Package '$($package.Id)' does not declare icon.png as its package icon.")
    }

    if ($null -eq $repository -or
        $repository.GetAttribute("url") -ne "https://github.com/litenova/LiteBus" -or
        $repository.GetAttribute("type") -ne "git") {
        $errors.Add("Package '$($package.Id)' does not contain canonical git repository metadata.")
    }

    $packageTags = if ($null -eq $tags) {
        @()
    }
    else {
        @([Text.RegularExpressions.Regex]::Split($tags.InnerText.Trim(), "[;\s]+") |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }

    if ($packageTags -notcontains "litebus") {
        $errors.Add("Package '$($package.Id)' does not include the 'litebus' package tag.")
    }

    $dependencies = @($package.Metadata.SelectNodes("n:dependencies/n:group/n:dependency | n:dependencies/n:dependency", $package.NamespaceManager))

    foreach ($dependency in $dependencies) {
        $dependencyId = $dependency.GetAttribute("id")
        $dependencyVersion = $dependency.GetAttribute("version")

        if ($dependencyId -eq "LiteBus.Extensions.All") {
            $errors.Add("Package '$($package.Id)' depends on removed package 'LiteBus.Extensions.All'.")
        }

        if ($dependencyId.StartsWith("LiteBus.", [StringComparison]::Ordinal)) {
            if (-not $expectedPackageIds.Contains($dependencyId)) {
                $errors.Add("Package '$($package.Id)' depends on unknown LiteBus package '$dependencyId'.")
            }

            if ($dependencyVersion -ne $package.Version) {
                $errors.Add("Package '$($package.Id)' depends on '$dependencyId' version '$dependencyVersion' instead of '$($package.Version)'.")
            }
        }
    }

    $expectedSymbolPackage = "$($package.Id).$($package.Version).snupkg"
    $hasSymbolPackage = $symbolPackageFiles.Name -contains $expectedSymbolPackage

    if ($package.Id -eq "LiteBus.Analyzers") {
        if ($hasSymbolPackage) {
            $errors.Add("Analyzer package must not produce an empty symbol package.")
        }
    }
    elseif (-not $hasSymbolPackage) {
        $errors.Add("Package '$($package.Id)' does not have matching symbol package '$expectedSymbolPackage'.")
    }
}

foreach ($symbolPackageFile in $symbolPackageFiles) {
    $archive = [IO.Compression.ZipFile]::OpenRead($symbolPackageFile.FullName)

    try {
        $entries = @($archive.Entries | ForEach-Object FullName)
        $matchingPackage = $packageMetadata | Where-Object {
            $symbolPackageFile.Name -eq "$($_.Id).$($_.Version).snupkg"
        } | Select-Object -First 1

        if ($null -eq $matchingPackage) {
            $errors.Add("Unexpected symbol package '$($symbolPackageFile.Name)' was produced.")
            continue
        }

        $expectedPdb = "lib/net10.0/$($matchingPackage.Id).pdb"
        if ($entries -notcontains $expectedPdb) {
            $errors.Add("Symbol package '$($symbolPackageFile.Name)' does not contain '$expectedPdb'.")
        }
    }
    finally {
        $archive.Dispose()
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    throw "Package validation failed with $($errors.Count) error(s)."
}

Write-Host "Validated $($packageFiles.Count) packages and $($symbolPackageFiles.Count) symbol packages."
