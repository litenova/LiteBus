[CmdletBinding()]
param(
    [string] $Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$documentationRoot = Join-Path $repositoryRoot 'site/content/docs'
$documentationFiles = Get-ChildItem -LiteralPath $documentationRoot -Filter '*.md' -File -Recurse
$violations = [Collections.Generic.List[string]]::new()

function Add-Violation {
    param(
        [Parameter(Mandatory)]
        [string] $Path,
        [Parameter(Mandatory)]
        [int] $Line,
        [Parameter(Mandatory)]
        [string] $Message
    )

    $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $Path).Replace('\', '/')
    $violations.Add("${relativePath}:${Line}: ${Message}")
}

function New-OrdinalSet {
    $set = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    return ,$set
}

function Normalize-Snippet {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Content
    )

    $lines = [Collections.Generic.List[string]]::new()
    $lines.AddRange([string[]] [regex]::Split($Content.Replace("`r", ''), "`n"))

    while ($lines.Count -gt 0 -and [string]::IsNullOrWhiteSpace($lines[0])) {
        $lines.RemoveAt(0)
    }

    while ($lines.Count -gt 0 -and [string]::IsNullOrWhiteSpace($lines[$lines.Count - 1])) {
        $lines.RemoveAt($lines.Count - 1)
    }

    $indents = @($lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
        ([regex]::Match($_, '^\s*')).Length
    })
    $commonIndent = if ($indents.Count -eq 0) { 0 } else { ($indents | Measure-Object -Minimum).Minimum }
    $normalized = $lines | ForEach-Object {
        if ($_.Length -ge $commonIndent) { $_.Substring($commonIndent) } else { $_ }
    }

    return [string]::Join("`n", $normalized)
}

function Find-OwningProject {
    param(
        [Parameter(Mandatory)]
        [string] $SourcePath
    )

    $directory = [IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($SourcePath))

    while ($null -ne $directory -and
        $directory.FullName.StartsWith($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $project = Get-ChildItem -LiteralPath $directory.FullName -Filter '*.csproj' -File | Select-Object -First 1

        if ($null -ne $project) {
            return $project.FullName
        }

        $directory = $directory.Parent
    }

    return $null
}

# Validate every documented TestClassTests.TestMethod reference against test discovery or its declaring source file.
$testListOutput = & dotnet test (Join-Path $repositoryRoot 'LiteBus.slnx') `
    --configuration $Configuration `
    --no-build `
    --no-restore `
    --list-tests 2>&1

if ($LASTEXITCODE -ne 0) {
    throw "Test discovery failed with exit code $LASTEXITCODE.`n$($testListOutput -join [Environment]::NewLine)"
}

$discoveredTests = New-OrdinalSet

foreach ($line in $testListOutput) {
    $candidate = ([string] $line).Trim()

    if ($candidate -match '^LiteBus\..+Tests\.[A-Za-z_][A-Za-z0-9_]*(?:\(.*\))?$') {
        [void] $discoveredTests.Add($candidate)
    }
}

$testReferencePattern = [regex]'(?<![A-Za-z0-9_])(?<class>[A-Za-z][A-Za-z0-9_]*Tests)\.(?<method>[A-Za-z_][A-Za-z0-9_]*)(?![A-Za-z0-9_])'
$testSourceFiles = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'tests') -Filter '*.cs' -File -Recurse
$testSourceFilesByName = @{}

foreach ($sourceFile in $testSourceFiles) {
    if (-not $testSourceFilesByName.ContainsKey($sourceFile.BaseName)) {
        $testSourceFilesByName[$sourceFile.BaseName] = [Collections.Generic.List[IO.FileInfo]]::new()
    }

    $testSourceFilesByName[$sourceFile.BaseName].Add($sourceFile)
}

foreach ($file in $documentationFiles) {
    $lines = [regex]::Split([IO.File]::ReadAllText($file.FullName), '\r?\n')

    for ($lineIndex = 0; $lineIndex -lt $lines.Length; $lineIndex++) {
        foreach ($match in $testReferencePattern.Matches($lines[$lineIndex])) {
            $className = $match.Groups['class'].Value
            $methodName = $match.Groups['method'].Value

            if ([string]::Equals($methodName, 'cs', [StringComparison]::Ordinal) -or
                $className -in @('UnitTests', 'IntegrationTests')) {
                continue
            }

            $reference = "$className.$methodName"
            $isDiscovered = $false

            foreach ($discoveredTest in $discoveredTests) {
                if ($discoveredTest.EndsWith(".$reference", [StringComparison]::Ordinal) -or
                    $discoveredTest.Contains(".$reference(", [StringComparison]::Ordinal)) {
                    $isDiscovered = $true
                    break
                }
            }

            if ($isDiscovered) {
                continue
            }

            $isDeclared = $false

            if ($testSourceFilesByName.ContainsKey($className)) {
                foreach ($sourceFile in $testSourceFilesByName[$className]) {
                    $sourceContent = [IO.File]::ReadAllText($sourceFile.FullName)

                    if ($sourceContent -match "\b$([regex]::Escape($methodName))\s*\(") {
                        $isDeclared = $true
                        break
                    }
                }
            }

            if (-not $isDeclared) {
                Add-Violation `
                    -Path $file.FullName `
                    -Line ($lineIndex + 1) `
                    -Message "Referenced test symbol '$reference' was not discovered or found in its declaring source file."
            }
        }
    }
}

# Keep the analyzer rule table aligned with the diagnostic IDs compiled into LiteBus.Analyzers.
$diagnosticIdsPath = Join-Path $repositoryRoot 'src/LiteBus.Analyzers/DiagnosticIds.cs'
$analyzerDocumentationPath = Join-Path $documentationRoot 'reference/analyzers.md'
$implementedAnalyzerIds = New-OrdinalSet
$documentedAnalyzerIds = New-OrdinalSet
$reservedAnalyzerIds = New-OrdinalSet

foreach ($match in [regex]::Matches([IO.File]::ReadAllText($diagnosticIdsPath), '"(?<id>LB\d{4})"')) {
    [void] $implementedAnalyzerIds.Add($match.Groups['id'].Value)
}

foreach ($line in [regex]::Split([IO.File]::ReadAllText($analyzerDocumentationPath), '\r?\n')) {
    if ($line -match '^\| (?<id>LB\d{4}) \| (?<severity>[^|]+) \|') {
        $id = $Matches['id']
        $severity = $Matches['severity'].Trim()

        if ([string]::Equals($severity, 'Reserved', [StringComparison]::Ordinal)) {
            [void] $reservedAnalyzerIds.Add($id)
        }
        else {
            [void] $documentedAnalyzerIds.Add($id)
        }
    }
}

if (-not $implementedAnalyzerIds.SetEquals($documentedAnalyzerIds)) {
    Add-Violation `
        -Path $analyzerDocumentationPath `
        -Line 1 `
        -Message "Analyzer inventory does not match compiled diagnostic IDs. Code: $($implementedAnalyzerIds -join ', '). Docs: $($documentedAnalyzerIds -join ', ')."
}

if (-not $reservedAnalyzerIds.SetEquals([string[]] @('LB1002', 'LB1006'))) {
    Add-Violation `
        -Path $analyzerDocumentationPath `
        -Line 1 `
        -Message "Reserved analyzer inventory must contain exactly LB1002 and LB1006, the IDs that were reserved and never shipped. Found: $($reservedAnalyzerIds -join ', ')."
}

# docs/CONTEXT.md lists the analyzer inventory in one line; it must name the same IDs as DiagnosticIds.
$contextPath = Join-Path $repositoryRoot 'docs/CONTEXT.md'
$contextInventoryLine = [regex]::Split([IO.File]::ReadAllText($contextPath), '?
') |
    Where-Object { $_.StartsWith('`LB1001` DuplicateCommandHandler', [StringComparison]::Ordinal) } |
    Select-Object -First 1

if ($null -eq $contextInventoryLine) {
    Add-Violation -Path $contextPath -Line 1 -Message 'docs/CONTEXT.md has no analyzer inventory line starting with `LB1001` DuplicateCommandHandler.'
}
else {
    $listedPart = $contextInventoryLine.Split('(')[0]
    $contextAnalyzerIds = New-OrdinalSet

    foreach ($match in [regex]::Matches($listedPart, '`(?<id>LB\d{4})`')) {
        [void] $contextAnalyzerIds.Add($match.Groups['id'].Value)
    }

    if (-not $implementedAnalyzerIds.SetEquals($contextAnalyzerIds)) {
        Add-Violation `
            -Path $contextPath `
            -Line 1 `
            -Message "docs/CONTEXT.md analyzer inventory does not match compiled diagnostic IDs. Code: $($implementedAnalyzerIds -join ', '). Docs: $($contextAnalyzerIds -join ', ')."
    }
}

# Keep the dependency-graph package table aligned with the generated package inventory.
$dependencyGraphPath = Join-Path $documentationRoot 'architecture/dependency-graph.md'
$packageInventoryDocPath = Join-Path $documentationRoot 'architecture/generated-package-inventory.md'

function Get-PackageTableRows {
    param(
        [Parameter(Mandatory)]
        [string] $Path,
        [Parameter(Mandatory)]
        [string] $SectionHeading,
        [string] $EndHeading = $null
    )

    $rows = [Collections.Generic.List[string]]::new()
    $inSection = $false

    foreach ($line in [regex]::Split([IO.File]::ReadAllText($Path), '\r?\n')) {
        if (-not $inSection) {
            if ($line.StartsWith($SectionHeading, [StringComparison]::Ordinal)) {
                $inSection = $true
            }

            continue
        }

        if (-not [string]::IsNullOrEmpty($EndHeading) -and $line.StartsWith($EndHeading, [StringComparison]::Ordinal)) {
            break
        }

        if ($line.StartsWith('| `LiteBus', [StringComparison]::Ordinal)) {
            $rows.Add($line)
        }
    }

    return ,$rows
}

function Get-ReferenceSet {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Cell
    )

    $set = New-OrdinalSet

    if ([string]::Equals($Cell.Trim(), 'none', [StringComparison]::OrdinalIgnoreCase)) {
        return ,$set
    }

    foreach ($entry in $Cell.Trim() -split ',') {
        $value = $entry.Trim().Trim('`')

        if ($value.Length -gt 0) {
            [void] $set.Add($value)
        }
    }

    return ,$set
}

$inventoryByPackage = @{}
$inventoryOrder = [Collections.Generic.List[string]]::new()

foreach ($row in (Get-PackageTableRows -Path $packageInventoryDocPath -SectionHeading '# Generated Package Inventory')) {
    $cells = $row -split '\|'
    $package = $cells[1].Trim().Trim('`')
    $inventoryByPackage[$package] = @((Get-ReferenceSet -Cell $cells[2]), (Get-ReferenceSet -Cell $cells[3]))
    $inventoryOrder.Add($package)
}

$graphByPackage = @{}

foreach ($row in (Get-PackageTableRows -Path $dependencyGraphPath -SectionHeading '## Package Table' -EndHeading '## Dependency Rules')) {
    $cells = $row -split '\|'

    if ($cells.Count -lt 6) {
        continue
    }

    $package = $cells[1].Trim().Trim('`')

    if ($package.Contains('*')) {
        continue
    }

    $graphByPackage[$package] = @((Get-ReferenceSet -Cell $cells[3]), (Get-ReferenceSet -Cell $cells[4]))
}

$inventoryPackages = New-OrdinalSet
$graphPackages = New-OrdinalSet

foreach ($package in $inventoryByPackage.Keys) {
    [void] $inventoryPackages.Add($package)
}

foreach ($package in $graphByPackage.Keys) {
    [void] $graphPackages.Add($package)
}

if (-not $inventoryPackages.SetEquals($graphPackages)) {
    $missing = @($inventoryPackages | Where-Object { -not $graphPackages.Contains($_) })
    $extra = @($graphPackages | Where-Object { -not $inventoryPackages.Contains($_) })
    Add-Violation `
        -Path $dependencyGraphPath `
        -Line 1 `
        -Message "Package table does not match the generated package inventory. Missing: $($missing -join ', '). Extra: $($extra -join ', ')."
}

foreach ($package in $inventoryOrder) {
    if (-not $graphByPackage.ContainsKey($package)) {
        continue
    }

    if (-not $inventoryByPackage[$package][0].SetEquals($graphByPackage[$package][0])) {
        Add-Violation `
            -Path $dependencyGraphPath `
            -Line 1 `
            -Message "Package '$package' project references do not match the generated package inventory. Code: $($inventoryByPackage[$package][0] -join ', '). Docs: $($graphByPackage[$package][0] -join ', ')."
    }

    if (-not $inventoryByPackage[$package][1].SetEquals($graphByPackage[$package][1])) {
        Add-Violation `
            -Path $dependencyGraphPath `
            -Line 1 `
            -Message "Package '$package' package references do not match the generated package inventory. Code: $($inventoryByPackage[$package][1] -join ', '). Docs: $($graphByPackage[$package][1] -join ', ')."
    }
}

# Keep the architecture metric catalog aligned with the public telemetry constants.
$telemetrySources = @(
    'src/LiteBus.Messaging/LiteBusMediationTelemetry.cs',
    'src/LiteBus.Inbox/LiteBusInboxTelemetry.cs',
    'src/LiteBus.Outbox/LiteBusOutboxTelemetry.cs',
    'src/LiteBus.Inbox.Ingress/LiteBusInboxIngressTelemetry.cs',
    'src/LiteBus.Transport/LiteBusTransportTelemetry.cs'
)

$implementedInstruments = New-OrdinalSet

foreach ($relativePath in $telemetrySources) {
    $source = [IO.File]::ReadAllText((Join-Path $repositoryRoot $relativePath))

    foreach ($match in [regex]::Matches($source, 'public const string (?<name>\w+InstrumentName)\s*=\s*"(?<value>[^"]+)"')) {
        [void] $implementedInstruments.Add($match.Groups['value'].Value)
    }
}

$architectureDocPath = Join-Path $documentationRoot 'architecture/README.md'
$inMetricCatalog = $false
$documentedInstruments = New-OrdinalSet

foreach ($line in [regex]::Split([IO.File]::ReadAllText($architectureDocPath), '\r?\n')) {
    if (-not $inMetricCatalog) {
        if ($line.StartsWith('### OpenTelemetry Metric Catalog', [StringComparison]::Ordinal)) {
            $inMetricCatalog = $true
        }

        continue
    }

    if ($line.StartsWith('## ', [StringComparison]::Ordinal)) {
        break
    }

    if ($line -match '^\| `[^`]+` \| `(?<instrument>[^`]+)` \| `[^`]+` \|') {
        [void] $documentedInstruments.Add($Matches['instrument'])
    }
}

if (-not $implementedInstruments.SetEquals($documentedInstruments)) {
    Add-Violation `
        -Path $architectureDocPath `
        -Line 1 `
        -Message "Metric catalog does not match the public telemetry instruments. Code: $($implementedInstruments -join ', '). Docs: $($documentedInstruments -join ', ')."
}

# Keep the documented management route table aligned with the endpoint mapper.
# Both axis route bodies and the health route are read from source, so no route is hard-coded.
$managementSourcePath = Join-Path $repositoryRoot 'src/LiteBus.Extensions.AspNetCore/LiteBusManagementEndpointExtensions.cs'
$managementDocumentationPath = Join-Path $documentationRoot 'catalog/hosting/aspnet-management-endpoints.md'
$managementSource = [IO.File]::ReadAllText($managementSourcePath)
$inboxBlockMatch = [regex]::Match(
    $managementSource,
    '(?s)private static void MapInboxManagementEndpoints\(.*?\)\s*\{(?<body>.*?)\r?\n    \}')

if (-not $inboxBlockMatch.Success) {
    throw 'Could not locate MapInboxManagementEndpoints for route validation.'
}

$implementedRoutes = New-OrdinalSet

foreach ($match in [regex]::Matches(
    $inboxBlockMatch.Groups['body'].Value,
    '\.Map(?<method>Get|Post|Delete)\("(?<route>[^"]+)"')) {
    $method = $match.Groups['method'].Value.ToUpperInvariant()
    $route = $match.Groups['route'].Value -replace ':[^}]+', ''
    [void] $implementedRoutes.Add("$method /litebus/inbox$route")
}

$outboxBlockMatch = [regex]::Match(
    $managementSource,
    '(?s)private static void MapOutboxManagementEndpoints\(.*?\)\s*\{(?<body>.*?)\r?\n    \}')

if (-not $outboxBlockMatch.Success) {
    throw 'Could not locate MapOutboxManagementEndpoints for route validation.'
}

$implementedOutboxRoutes = New-OrdinalSet

foreach ($match in [regex]::Matches(
    $outboxBlockMatch.Groups['body'].Value,
    '\.Map(?<method>Get|Post|Delete)\("(?<route>[^"]+)"')) {
    $method = $match.Groups['method'].Value.ToUpperInvariant()
    $route = $match.Groups['route'].Value -replace ':[^}]+', ''
    [void] $implementedOutboxRoutes.Add("$method /litebus/outbox$route")
}

$managementOptionsPath = Join-Path $repositoryRoot 'src/LiteBus.Extensions.AspNetCore/LiteBusManagementOptions.cs'
$optionsSource = [IO.File]::ReadAllText($managementOptionsPath)
$prefixMatch = [regex]::Match($optionsSource, 'public string RoutePrefix \{ get; set; \} = "(?<prefix>[^"]+)"')

if (-not $prefixMatch.Success) {
    throw 'Could not locate the default management route prefix for route validation.'
}

$healthRouteMatch = [regex]::Match($managementSource, 'endpoints\.MapGet\(\s*BuildManagementRoute\(prefix,\s*"health"\)')

if (-not $healthRouteMatch.Success) {
    Add-Violation `
        -Path $managementSourcePath `
        -Line 1 `
        -Message 'Health route mapping was not found in the endpoint mapper.'
}

[void] $implementedRoutes.Add("GET /$($prefixMatch.Groups['prefix'].Value)/health")
$documentedRoutes = New-OrdinalSet

foreach ($line in [regex]::Split([IO.File]::ReadAllText($managementDocumentationPath), '\r?\n')) {
    if ($line -match '^\| `(?<route>/litebus/(?:inbox|health)[^`]*)` \| `(?<method>GET|POST|DELETE)` \|') {
        [void] $documentedRoutes.Add("$($Matches['method']) $($Matches['route'])")
    }
}

if (-not $implementedRoutes.SetEquals($documentedRoutes)) {
    Add-Violation `
        -Path $managementDocumentationPath `
        -Line 1 `
        -Message "Management route table does not match endpoint mappings. Code: $($implementedRoutes -join ', '). Docs: $($documentedRoutes -join ', ')."
}

$expectedOutboxRoutes = New-OrdinalSet

foreach ($route in $implementedRoutes) {
    if ($route.StartsWith('GET /litebus/health', [StringComparison]::Ordinal)) {
        continue
    }

    [void] $expectedOutboxRoutes.Add($route.Replace('/litebus/inbox', '/litebus/outbox'))
}

if (-not $implementedOutboxRoutes.SetEquals($expectedOutboxRoutes)) {
    Add-Violation `
        -Path $managementSourcePath `
        -Line 1 `
        -Message "Outbox management routes do not mirror inbox routes. Inbox: $($expectedOutboxRoutes -join ', '). Outbox: $($implementedOutboxRoutes -join ', ')."
}

# Source-linked snippets are copied from compiled projects, then checked byte-for-byte after common indentation removal.
$snippetPattern = [regex]'(?s)<!--\s*snippet-source:\s*(?<path>[^#\s]+)#(?<id>[A-Za-z0-9_-]+)\s*-->\s*```csharp\s*\r?\n(?<code>.*?)\r?\n```'
$snippetCount = 0
$snippetProjects = New-OrdinalSet

foreach ($file in $documentationFiles) {
    $content = [IO.File]::ReadAllText($file.FullName)

    foreach ($match in $snippetPattern.Matches($content)) {
        $contentBeforeMarker = $content.Substring(0, $match.Index)
        $precedingFenceCount = [regex]::Matches($contentBeforeMarker, '(?m)^\s*(?:`{3,}|~{3,})').Count

        if ($precedingFenceCount % 2 -ne 0) {
            continue
        }

        $snippetCount++
        $relativeSourcePath = $match.Groups['path'].Value.Replace('/', [IO.Path]::DirectorySeparatorChar)
        $sourcePath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $relativeSourcePath))
        $snippetId = $match.Groups['id'].Value

        if (-not $sourcePath.StartsWith(
            $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            Add-Violation -Path $file.FullName -Line 1 -Message "Snippet source does not exist: '$relativeSourcePath'."
            continue
        }

        $sourceLines = [regex]::Split([IO.File]::ReadAllText($sourcePath), '\r?\n')
        $startMarker = "// <docs-snippet $snippetId>"
        $endMarker = "// </docs-snippet>"
        $startIndex = -1
        $endIndex = -1

        for ($index = 0; $index -lt $sourceLines.Length; $index++) {
            if ($sourceLines[$index].Trim() -ceq $startMarker) {
                $startIndex = $index + 1
                continue
            }

            if ($startIndex -ge 0 -and $sourceLines[$index].Trim() -ceq $endMarker) {
                $endIndex = $index
                break
            }
        }

        if ($startIndex -lt 0 -or $endIndex -lt $startIndex) {
            Add-Violation -Path $file.FullName -Line 1 -Message "Snippet '$snippetId' was not found in '$relativeSourcePath'."
            continue
        }

        $sourceSnippet = Normalize-Snippet -Content ([string]::Join("`n", $sourceLines[$startIndex..($endIndex - 1)]))
        $documentedSnippet = Normalize-Snippet -Content $match.Groups['code'].Value

        if (-not [string]::Equals($sourceSnippet, $documentedSnippet, [StringComparison]::Ordinal)) {
            Add-Violation -Path $file.FullName -Line 1 -Message "Snippet '$snippetId' is stale relative to '$relativeSourcePath'."
        }

        $owningProject = Find-OwningProject -SourcePath $sourcePath

        if ($null -eq $owningProject) {
            Add-Violation -Path $file.FullName -Line 1 -Message "Snippet source '$relativeSourcePath' is not owned by a compilable project."
        }
        else {
            [void] $snippetProjects.Add($owningProject)
        }
    }
}

if ($snippetCount -eq 0) {
    Add-Violation -Path (Join-Path $documentationRoot 'getting-started/README.md') -Line 1 -Message 'No source-linked C# documentation snippets were found.'
}

foreach ($project in $snippetProjects) {
    $buildOutput = & dotnet build $project `
        --configuration $Configuration `
        --no-restore `
        --no-dependencies 2>&1

    if ($LASTEXITCODE -ne 0) {
        Add-Violation `
            -Path $project `
            -Line 1 `
            -Message "Documentation snippet project did not compile: $($buildOutput -join [Environment]::NewLine)"
    }
}

if ($violations.Count -gt 0) {
    $violations | Sort-Object -Unique | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    throw "Documentation semantic validation failed with $($violations.Count) violation(s)."
}

Write-Host "Documentation semantic validation passed for $snippetCount compiled snippet(s), $($discoveredTests.Count) discovered tests, $($implementedAnalyzerIds.Count) analyzer IDs, and $($implementedRoutes.Count) management routes."
