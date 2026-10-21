#Requires -Version 7
<#
.SYNOPSIS
Downloads the pinned git bundle for one runtime id into resources/git/<rid>/.

.DESCRIPTION
Reads build/git-manifest.json, downloads the archive to .tools/downloads/, refuses it unless its
SHA-256 matches the manifest, extracts it beside the final folder and renames it into place.
A stamp file inside the bundle records version and hash, so a re-run with a matching stamp
changes nothing. Safe to run from several processes at once (parallel project builds).

.PARAMETER RuntimeId
win-x64, win-arm64, osx-x64, osx-arm64, linux-x64 or linux-arm64. Defaults to this machine.

.PARAMETER Force
Fetch and extract again even when the stamp matches.
#>
[CmdletBinding()]
param(
    [string]$RuntimeId,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $PSScriptRoot 'git-manifest.json'
$downloadsDir = Join-Path $repoRoot '.tools/downloads'
$bundlesDir = Join-Path $repoRoot 'resources/git'
$stampName = '.savepoint-git-stamp'

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

function Test-StampMatches([string]$StampPath, [hashtable]$Entry, [string]$BundleDir) {
    if (-not (Test-Path -LiteralPath $StampPath)) { return $false }
    if (-not (Test-Path -LiteralPath (Join-Path $BundleDir $Entry.entry))) { return $false }
    $lines = Get-Content -LiteralPath $StampPath
    foreach ($expected in @("version=$($Entry.version)", "sha256=$($Entry.sha256)", "layout=$($Entry.layout)", "entry=$($Entry.entry)")) {
        if ($lines -cnotcontains $expected) { return $false }
    }
    return $true
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

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
if (-not $manifest.ContainsKey($RuntimeId)) {
    throw "No bundled git for runtime id '$RuntimeId'. Known ids: $(($manifest.Keys | Sort-Object) -join ', ')."
}
$entry = $manifest[$RuntimeId]

$bundleDir = Join-Path $bundlesDir $RuntimeId
$stampPath = Join-Path $bundleDir $stampName

function Set-StampCurrent {
    # MSBuild compares the stamp's timestamp with the manifest, so a no-op run refreshes it.
    (Get-Item -LiteralPath $stampPath).LastWriteTimeUtc = [DateTime]::UtcNow
    Write-Host "git $($entry.version) for $RuntimeId is up to date"
}

if (-not $Force -and (Test-StampMatches $stampPath $entry $bundleDir)) {
    Set-StampCurrent
    return
}

New-Item -ItemType Directory -Path $downloadsDir -Force | Out-Null
New-Item -ItemType Directory -Path $bundlesDir -Force | Out-Null

$lock = Enter-FetchLock (Join-Path $downloadsDir "$RuntimeId.lock")
$staging = Join-Path $bundlesDir ".$RuntimeId.staging-$PID"
try {
    # Another process may have finished the same fetch while this one waited.
    if (-not $Force -and (Test-StampMatches $stampPath $entry $bundleDir)) {
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

    # The stamp goes in last: a bundle without one is treated as unfinished.
    Set-Content -LiteralPath (Join-Path $staging $stampName) -Encoding utf8NoBOM -Value @(
        "version=$($entry.version)"
        "sha256=$($entry.sha256)"
        "layout=$($entry.layout)"
        "entry=$($entry.entry)"
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
