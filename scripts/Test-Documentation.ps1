[CmdletBinding()]
param()

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$documentationRoot = Join-Path $repositoryRoot 'docs'
$samplesRoot = Join-Path $repositoryRoot 'samples'
$documentationFiles = @(
    Get-Item -LiteralPath (Join-Path $repositoryRoot 'README.md')
    Get-Item -LiteralPath (Join-Path $repositoryRoot 'Changelog.md')
    Get-ChildItem -LiteralPath $documentationRoot -Filter '*.md' -File -Recurse
    Get-ChildItem -LiteralPath $samplesRoot -Filter '*.md' -File -Recurse
)

$violations = [Collections.Generic.List[string]]::new()
$bannedPhrases = @(
    'seamless',
    'powerful',
    'modern',
    'production-grade',
    'best-in-class',
    'state-of-the-art',
    'cutting-edge',
    'innovative',
    'groundbreaking',
    'game-changer',
    'tapestry',
    'multifaceted',
    'synergy',
    'holistic',
    'streamlined',
    'it is important to note that',
    "it's worth noting",
    'please note that',
    'this underscores the importance of',
    'it cannot be denied that',
    'as of my knowledge cutoff',
    'in order to',
    'but here''s the catch',
    'as we can see',
    'as mentioned above',
    'as noted earlier',
    'at its core',
    'under the hood',
    'in today''s fast-paced world',
    'in this ever-evolving landscape',
    'in the digital age',
    'in conclusion',
    'to summarize',
    'let''s delve into',
    'delve deeper',
    'delve',
    'i''d be happy to',
    'certainly',
    'absolutely',
    'indeed',
    'great question',
    'of course',
    'happy to help'
)
$linkPattern = [regex]'!??\[[^\]]*\]\((?<target><[^>]+>|[^\s\)]+)(?:\s+["''][^"'']*["''])?\)'

function Add-Violation {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [int]$Line,
        [Parameter(Mandatory)]
        [string]$Message
    )

    $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $Path).Replace('\', '/')
    $violations.Add("${relativePath}:${Line}: ${Message}")
}

function Test-ExactPathCase {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $Path)
    $currentPath = $repositoryRoot

    foreach ($segment in $relativePath.Split([IO.Path]::DirectorySeparatorChar, [StringSplitOptions]::RemoveEmptyEntries)) {
        $entry = Get-ChildItem -LiteralPath $currentPath -Force | Where-Object Name -CEQ $segment | Select-Object -First 1
        if ($null -eq $entry) {
            return $false
        }

        $currentPath = $entry.FullName
    }

    return $true
}

$dependencyGraphPath = Join-Path $documentationRoot 'architecture/dependency-graph.md'
$dependencyGraphContent = [IO.File]::ReadAllText($dependencyGraphPath)
$sourceProjects = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Filter '*.csproj' -File -Recurse

foreach ($project in $sourceProjects) {
    if ($dependencyGraphContent.IndexOf($project.BaseName, [StringComparison]::Ordinal) -lt 0) {
        Add-Violation -Path $dependencyGraphPath -Line 1 -Message "Package inventory is missing shipping project '$($project.BaseName)'."
    }
}

foreach ($file in $documentationFiles) {
    $content = [IO.File]::ReadAllText($file.FullName)
    $lines = [regex]::Split($content, '\r?\n')
    $insideCodeFence = $false
    $topLevelHeadingLines = [Collections.Generic.List[int]]::new()

    for ($lineIndex = 0; $lineIndex -lt $lines.Length; $lineIndex++) {
        $line = $lines[$lineIndex]
        $lineNumber = $lineIndex + 1

        if ($line -match '^\s*(```|~~~)') {
            $insideCodeFence = -not $insideCodeFence
        }
        elseif (-not $insideCodeFence -and $line -match '^# ') {
            $topLevelHeadingLines.Add($lineNumber)
        }

        if ($line -match '[ \t]+$') {
            Add-Violation -Path $file.FullName -Line $lineNumber -Message 'Trailing whitespace is not allowed.'
        }

        if ($line -match '[^\x00-\x7F]') {
            Add-Violation -Path $file.FullName -Line $lineNumber -Message 'Non-ASCII typography is not allowed.'
        }

        foreach ($phrase in $bannedPhrases) {
            if ($line.Contains($phrase, [StringComparison]::OrdinalIgnoreCase)) {
                Add-Violation -Path $file.FullName -Line $lineNumber -Message "Banned phrase: '$phrase'."
            }
        }

        if ($line.Contains('[[', [StringComparison]::Ordinal) -or
            $line.Contains('LiteBus.wiki', [StringComparison]::OrdinalIgnoreCase) -or
            $line.Contains('github.com/litenova/LiteBus/wiki', [StringComparison]::OrdinalIgnoreCase)) {
            Add-Violation -Path $file.FullName -Line $lineNumber -Message 'Wiki references are not allowed in repository documentation.'
        }

        if (-not $insideCodeFence -and $line.StartsWith(':::', [StringComparison]::Ordinal)) {
            Add-Violation -Path $file.FullName -Line $lineNumber -Message 'Unsupported Markdown directives are not allowed. Use standard Markdown.'
        }

        foreach ($match in $linkPattern.Matches($line)) {
            $target = $match.Groups['target'].Value.Trim('<', '>')
            if ($target.StartsWith('#', [StringComparison]::Ordinal) -or
                $target.StartsWith('http://', [StringComparison]::OrdinalIgnoreCase) -or
                $target.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase) -or
                $target.StartsWith('mailto:', [StringComparison]::OrdinalIgnoreCase) -or
                $target.StartsWith('data:', [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            $pathPart = $target -replace '[#?].*$', ''
            $decodedPath = [Uri]::UnescapeDataString($pathPart)
            $resolvedPath = [IO.Path]::GetFullPath((Join-Path $file.DirectoryName $decodedPath))

            if (-not $resolvedPath.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                Add-Violation -Path $file.FullName -Line $lineNumber -Message "Local link escapes the repository: '$target'."
                continue
            }

            if (-not (Test-Path -LiteralPath $resolvedPath)) {
                Add-Violation -Path $file.FullName -Line $lineNumber -Message "Local link target does not exist: '$target'."
                continue
            }

            if (-not (Test-ExactPathCase -Path $resolvedPath)) {
                Add-Violation -Path $file.FullName -Line $lineNumber -Message "Local link path casing does not match the filesystem: '$target'."
            }
        }
    }

    if ($topLevelHeadingLines.Count -ne 1) {
        $headingLines = $topLevelHeadingLines -join ', '
        $message = "Document must contain exactly one top-level heading outside code fences; found $($topLevelHeadingLines.Count)."

        if ($headingLines.Length -gt 0) {
            $message += " Heading lines: $headingLines."
        }

        Add-Violation -Path $file.FullName -Line 1 -Message $message
    }

    if (-not $content.EndsWith("`n", [StringComparison]::Ordinal)) {
        Add-Violation -Path $file.FullName -Line $lines.Length -Message 'File must end with one newline.'
    }

    if ($content.EndsWith("`n`n", [StringComparison]::Ordinal)) {
        Add-Violation -Path $file.FullName -Line $lines.Length -Message 'File must not end with a blank line.'
    }
}

if ($violations.Count -gt 0) {
    $violations | Sort-Object | ForEach-Object { Write-Error $_ }
    throw "Documentation validation failed with $($violations.Count) violation(s)."
}

Write-Host "Documentation validation passed for $($documentationFiles.Count) Markdown files."
