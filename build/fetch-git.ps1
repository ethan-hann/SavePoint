#Requires -Version 7
<#
.SYNOPSIS
Downloads the pinned git bundle for one runtime id into resources/git/<rid>/.

.DESCRIPTION
Reads build/git-manifest.json, downloads the archive to .tools/downloads/, refuses it unless its
SHA-256 matches the manifest, extracts it beside the final folder, deletes what the layout's
"prune" list names (Git Credential Manager, Git LFS, the http transport) and renames it into place.
A stamp file inside the bundle records version, hash, stamp format and the prune list that was applied,
so a re-run with a matching stamp changes nothing. Safe to run from several processes at once.

A prune pattern is a path relative to the bundle root, written with forward slashes. * and ? match
within one path segment and never cross a slash; anything else is literal. A match is deleted with
everything below it, and folders the deletion leaves empty go too. A pattern that would remove the
entry point stops the script.

.PARAMETER RuntimeId
win-x64, win-arm64, osx-x64, osx-arm64, linux-x64 or linux-arm64. Defaults to this machine.

.PARAMETER Force
Fetch and extract again even when the stamp matches.

.PARAMETER PruneDirectory
Only prune this folder with the runtime id's prune list, then stop. No download, no stamp.

.PARAMETER ManifestPath
Read this manifest instead of build/git-manifest.json.
#>
[CmdletBinding()]
param(
    [string]$RuntimeId,
    [switch]$Force,
    [string]$PruneDirectory,
    [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ManifestPath) { $ManifestPath = Join-Path $PSScriptRoot 'git-manifest.json' }
$downloadsDir = Join-Path $repoRoot '.tools/downloads'
$bundlesDir = Join-Path $repoRoot 'resources/git'
$stampName = '.savepoint-git-stamp'
# Bumped when the stamp or the prune step changes, so bundles extracted by an older script are made again.
$stampFormat = 2

function Get-HostRuntimeId {
    if ($IsWindows) { $os = 'win' }
    elseif ($IsMacOS) { $os = 'osx' }
    elseif ($IsLinux) { $os = 'linux' }
    else { throw 'Unsupported operating system.' }
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    return "$os-$arch"
}

function Get-FileSha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-StampMatches([string]$StampPath, [hashtable]$Entry, [string]$BundleDir, [string]$PruneHash) {
    if (-not (Test-Path -LiteralPath $StampPath)) { return $false }
    if (-not (Test-Path -LiteralPath (Join-Path $BundleDir $Entry.entry))) { return $false }
    $lines = Get-Content -LiteralPath $StampPath
    foreach ($expected in @("version=$($Entry.version)", "sha256=$($Entry.sha256)", "layout=$($Entry.layout)", "entry=$($Entry.entry)", "format=$stampFormat", "prune=$PruneHash")) {
        if ($lines -cnotcontains $expected) { return $false }
    }
    return $true
}

function Get-PrunePatterns([hashtable]$Manifest, [string]$Layout) {
    $patterns = $Manifest['layouts']?[$Layout]?['prune']
    if (-not $patterns -or $patterns.Count -eq 0) {
        throw "The manifest has no prune list for layout '$Layout'."
    }
    foreach ($pattern in $patterns) {
        $problem = $null
        if ([string]::IsNullOrWhiteSpace($pattern)) { $problem = 'is empty' }
        elseif ($pattern.Contains('\') -or $pattern -match '[:\[\]`]' -or $pattern -match '\p{Cc}') { $problem = 'must use / and plain characters only' }
        elseif ($pattern.StartsWith('/')) { $problem = 'must be relative to the bundle' }
        elseif (($pattern -split '/') -contains '' -or ($pattern -split '/') -contains '.' -or ($pattern -split '/') -contains '..') { $problem = 'has an empty, . or .. segment' }
        if ($problem) { throw "Prune pattern '$pattern' of layout '$Layout' $problem." }
    }
    return [string[]]$patterns
}

# The same text the shell script hashes (one pattern per line), so both scripts write the same stamp.
function Get-PruneHash([string[]]$Patterns) {
    $text = -join ($Patterns | ForEach-Object { "$_`n" })
    return [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant()
}

function Invoke-Prune([string]$Directory, [string[]]$Patterns, [string]$EntryPath) {
    $root = (Resolve-Path -LiteralPath $Directory).ProviderPath
    $targets = [System.Collections.Generic.List[string]]::new()
    # Patterns are resolved from inside the bundle so odd characters in its own path cannot act as wildcards.
    Push-Location -LiteralPath $root
    try {
        foreach ($pattern in $Patterns) {
            $native = $pattern.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
            foreach ($item in @(Get-Item -Path $native -Force -ErrorAction SilentlyContinue)) {
                $relative = [System.IO.Path]::GetRelativePath($root, $item.FullName).Replace('\', '/')
                if ($EntryPath -ieq $relative -or $EntryPath.StartsWith("$relative/", [System.StringComparison]::OrdinalIgnoreCase)) {
                    throw "Prune pattern '$pattern' would remove the entry point $EntryPath."
                }
                $targets.Add($item.FullName)
            }
        }
    }
    finally {
        Pop-Location
    }

    # Nothing is deleted until every match has been checked.
    $removed = 0
    foreach ($target in $targets) {
        # An earlier match may have taken this one with it.
        if (-not (Get-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue)) { continue }
        Remove-Item -LiteralPath $target -Recurse -Force
        $removed++

        $parent = [System.IO.Path]::GetDirectoryName($target)
        while ($parent.Length -gt $root.Length -and -not (Get-ChildItem -LiteralPath $parent -Force | Select-Object -First 1)) {
            Remove-Item -LiteralPath $parent -Force
            $parent = [System.IO.Path]::GetDirectoryName($parent)
        }
    }
    return $removed
}

function Enter-FetchLock([string]$Path) {
    $deadline = [DateTime]::UtcNow.AddMinutes(15)
    while ($true) {
        try {
            # The OS drops the handle if this process dies, so a lock can never go stale.
            return [System.IO.File]::Open($Path, 'OpenOrCreate', 'ReadWrite', 'None')
        }
        catch [System.IO.IOException] {
            if ([DateTime]::UtcNow -gt $deadline) { throw "Timed out waiting for another fetch of the git bundle ($Path)." }
            Start-Sleep -Milliseconds 500
        }
    }
}

function Save-Archive([hashtable]$Entry, [string]$ArchivePath) {
    if ($Entry.url -notmatch '^https://') { throw "Refusing a non-HTTPS download url: $($Entry.url)" }
    if ((Test-Path -LiteralPath $ArchivePath) -and ((Get-FileSha256 $ArchivePath) -eq $Entry.sha256)) {
        Write-Host "Using cached $([System.IO.Path]::GetFileName($ArchivePath))"
        return
    }
    Remove-Item -LiteralPath $ArchivePath -Force -ErrorAction SilentlyContinue

    $partPath = "$ArchivePath.part"
    Write-Host "Downloading $($Entry.url)"
    Invoke-WebRequest -Uri $Entry.url -OutFile $partPath -UserAgent 'savepoint-build' -MaximumRetryCount 3 -RetryIntervalSec 2

    $actual = Get-FileSha256 $partPath
    if ($actual -ne $Entry.sha256) {
        Remove-Item -LiteralPath $partPath -Force
        throw "SHA-256 mismatch for $($Entry.url): expected $($Entry.sha256) but got $actual. Nothing was extracted."
    }
    Move-Item -LiteralPath $partPath -Destination $ArchivePath -Force
}

function Expand-Bundle([hashtable]$Entry, [string]$ArchivePath, [string]$Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    switch ($Entry.archive) {
        'zip' {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [System.IO.Compression.ZipFile]::ExtractToDirectory($ArchivePath, $Destination)
        }
        'tar.gz' {
            # -p keeps the executable bits, --no-same-owner stops root runs from copying the archive's owner.
            & tar -xpzf $ArchivePath -C $Destination --no-same-owner
            if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE while extracting $ArchivePath" }
        }
        default { throw "Unsupported archive type '$($Entry.archive)'." }
    }
}

if (-not $RuntimeId) { $RuntimeId = Get-HostRuntimeId }

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -AsHashtable
# "layouts" and "sources" are not runtimes: the first says what to prune, the second pins the source archives a release publishes.
$knownIds = @($manifest.Keys | Where-Object { $_ -notin 'layouts', 'sources' } | Sort-Object)
if ($RuntimeId -notin $knownIds) {
    throw "No bundled git for runtime id '$RuntimeId'. Known ids: $($knownIds -join ', ')."
}
$entry = $manifest[$RuntimeId]
$prunePatterns = Get-PrunePatterns $manifest $entry.layout
$pruneHash = Get-PruneHash $prunePatterns

if ($PruneDirectory) {
    $count = Invoke-Prune $PruneDirectory $prunePatterns $entry.entry
    Write-Host "Pruned $count entries from $PruneDirectory"
    return
}

$bundleDir = Join-Path $bundlesDir $RuntimeId
$stampPath = Join-Path $bundleDir $stampName

function Set-StampCurrent {
    # MSBuild compares the stamp's timestamp with the manifest, so a no-op run refreshes it.
    (Get-Item -LiteralPath $stampPath).LastWriteTimeUtc = [DateTime]::UtcNow
    Write-Host "git $($entry.version) for $RuntimeId is up to date"
}

if (-not $Force -and (Test-StampMatches $stampPath $entry $bundleDir $pruneHash)) {
    Set-StampCurrent
    return
}

New-Item -ItemType Directory -Path $downloadsDir -Force | Out-Null
New-Item -ItemType Directory -Path $bundlesDir -Force | Out-Null

$lock = Enter-FetchLock (Join-Path $downloadsDir "$RuntimeId.lock")
$staging = Join-Path $bundlesDir ".$RuntimeId.staging-$PID"
try {
    # Another process may have finished the same fetch while this one waited.
    if (-not $Force -and (Test-StampMatches $stampPath $entry $bundleDir $pruneHash)) {
        Set-StampCurrent
        return
    }

    $archivePath = Join-Path $downloadsDir ([System.IO.Path]::GetFileName(([uri]$entry.url).AbsolutePath))
    Save-Archive $entry $archivePath

    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    Expand-Bundle $entry $archivePath $staging
    if (-not (Test-Path -LiteralPath (Join-Path $staging $entry.entry))) {
        throw "The archive for $RuntimeId does not contain $($entry.entry)."
    }

    $count = Invoke-Prune $staging $prunePatterns $entry.entry
    Write-Host "Pruned $count entries"

    # The stamp goes in last: a bundle without one is treated as unfinished.
    Set-Content -LiteralPath (Join-Path $staging $stampName) -Encoding utf8NoBOM -Value @(
        "version=$($entry.version)"
        "sha256=$($entry.sha256)"
        "layout=$($entry.layout)"
        "entry=$($entry.entry)"
        "format=$stampFormat"
        "prune=$pruneHash"
        "extracted=$([guid]::NewGuid())"
    )

    Remove-Item -LiteralPath $bundleDir -Recurse -Force -ErrorAction SilentlyContinue
    Move-Item -LiteralPath $staging -Destination $bundleDir
    Write-Host "Installed git $($entry.version) for $RuntimeId into $bundleDir"
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    $lock.Dispose()
}
