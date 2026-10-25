#Requires -Version 7
<#
.SYNOPSIS
Builds the Windows (win-x64) packages: a self-contained publish, then vpk pack.

.DESCRIPTION
Publishes src/Savepoint.App into artifacts/publish/win-x64, checks that the git bundle, the notices and the
license texts came along, and packs it with the pinned vpk (a local tool, never a global one) into
artifacts/release/win-x64: the setup, the portable zip and the full package. The version is the one <Version>
in Directory.Build.props. Runs at below-normal priority, like the gate.

Linux has its own script, build/package.sh. macOS is not produced here: vpk has to run on a Mac, and the result
has to be signed and notarized before anyone else can open it.

.PARAMETER Sources
Also downloads the source archives of the three tags build/git-manifest.json pins (the ones THIRD-PARTY-NOTICES.md
promises) into artifacts/release/source, prints their SHA-256 and compares each with the hash the manifest pins. This runs
before anything is built, so a failed check leaves no new package. An archive keeps its name only when it matches its
pin. A hash that is not pinned yet, or that differs, fails the run after every hash has been printed.
It also downloads the source archives of the MSYS2 and MINGW packages inside the Windows git bundle, which
build/msys2-sources.json lists with their pinned hashes, into artifacts/release/source/packages, and checks them the same way.

.PARAMETER AllowDirty
Packs a tree with uncommitted changes anyway, for a trial package. The build is then marked "-dirty". Without it the
run is refused, because a package from such a tree cannot be tied to a commit.
#>
[CmdletBinding()]
param(
    [switch]$Sources,
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot
# The CLI's tool cache lives in the workspace, like package.sh's: a cache in the user profile can point at a tree that no longer exists.
$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.tools/dotnet-cli-home'
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null

# Builds, vpk and the downloads all inherit this, so packaging never starves the desktop it runs on.
[System.Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'

$buildNodes = if ($env:SAVEPOINT_PACKAGE_BUILD_NODES) { $env:SAVEPOINT_PACKAGE_BUILD_NODES } else { '4' }
$rid = 'win-x64'
$publishDir = Join-Path $repoRoot "artifacts/publish/$rid"
$releaseDir = Join-Path $repoRoot "artifacts/release/$rid"
$icon = Join-Path $repoRoot 'src/Savepoint.App/Assets/Icon/savepoint.ico'

function Invoke-Step {
    param([string]$Name, [scriptblock]$Command)
    Write-Host "==> $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

function Get-SourceArchives {
    # The source archives a release publishes are the ones the manifest pins, beside the binaries they belong to.
    $listed = @(Get-TagSourcePins -ManifestPath (Join-Path $repoRoot 'build/git-manifest.json'))
    # The notices make the promise, so they have to name the same tags.
    $notices = Get-Content -LiteralPath (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Raw
    $tick = [char]96
    foreach ($source in $listed) {
        $promise = "$($source.Repository), tag $tick$($source.Tag)$tick"
        if (-not $notices.Contains($promise)) {
            throw "THIRD-PARTY-NOTICES.md does not promise source for $promise, which build/git-manifest.json pins for $($source.Key)."
        }
        $source
    }
}

. (Join-Path $PSScriptRoot 'package-sources.ps1')

function Get-PackageEntryNames {
    param([string]$Path)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try { @($archive.Entries | ForEach-Object { $_.FullName }) } finally { $archive.Dispose() }
}

# What vpk packed is what people install, so the packages are opened and the files the publish folder was checked for have to be in them.
function Assert-PackageHolds {
    param([string]$Path, [string]$ListingDir)
    $names = Get-PackageEntryNames $Path
    $leaf = Split-Path -Leaf $Path
    New-Item -ItemType Directory -Force -Path $ListingDir | Out-Null
    $names | Sort-Object | Set-Content -LiteralPath (Join-Path $ListingDir "$leaf.txt")
    Write-Host ('    {0}: {1} files (listed in {2})' -f $leaf, $names.Count, (Join-Path $ListingDir "$leaf.txt"))
    $needed = [ordered]@{
        'the git bundle'     = '(^|[/\\])git[/\\]\.savepoint-git-stamp$'
        'the notices'        = '(^|[/\\])THIRD-PARTY-NOTICES\.md$'
        # Only SavePoint's own file counts: the bundled git and libraries carry LICENSE files of their own, deeper in the package.
        # Velopack puts the app under lib/app/ in the nupkg and current/ in the portable zip.
        'the license'        = '^(lib/app|current)/LICENSE$'
        'the license texts'  = '(^|[/\\])licenses[/\\][^/\\]+$'
    }
    foreach ($what in $needed.Keys) {
        if (-not ($names | Where-Object { $_ -cmatch $needed[$what] })) {
            throw "$leaf does not hold $what."
        }
    }
}

try {
    $props = Join-Path $repoRoot 'Directory.Build.props'
    $version = (Select-Xml -Path $props -XPath '//Version').Node.InnerText.Trim()
    if (-not $version) { throw 'Directory.Build.props has no <Version>.' }
    # The setup installs into %LOCALAPPDATA%\<pack id> and clears that folder on install and uninstall, so SavePoint keeps no data there (D38). Builds packed as SavePointApp keep their data in %LOCALAPPDATA%\SavePoint, so this id must not be SavePoint until every install has moved it.
    $packId = (Select-Xml -Path $props -XPath '//SavepointWindowsPackId').Node.InnerText.Trim()
    if (-not $packId) { throw 'Directory.Build.props has no <SavepointWindowsPackId>.' }
    # Every build is the same version, so the commit is what tells two builds apart: it goes into the informational version, which
    # the startup log reads. A package from a tree with changes the commit does not hold cannot be tied to one.
    $commit = (git rev-parse --short=12 HEAD)
    if ($LASTEXITCODE -ne 0 -or -not $commit) { throw 'git could not say which commit this is.' }
    $changes = @(git status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw 'git could not say whether the working tree has changes.' }
    if ($changes.Count -gt 0 -and -not $AllowDirty) {
        throw "The working tree has $($changes.Count) uncommitted change(s), so the package could not be tied to a commit. Commit them, or pass -AllowDirty for a trial package."
    }
    $revision = if ($changes.Count -gt 0) { "$commit-dirty" } else { $commit }
    Write-Host "==> SavePoint $version ($revision) for $rid"

    if ($Sources) {
        $sourceDir = Join-Path $repoRoot 'artifacts/release/source'
        New-Item -ItemType Directory -Force -Path $sourceDir | Out-Null
        $ProgressPreference = 'SilentlyContinue'
        $problems = @()
        foreach ($source in Get-SourceArchives) {
            $name = ($source.Repository -replace '^https://github\.com/', '' -replace '/', '-') + "-$($source.Tag).tar.gz"
            $target = Join-Path $sourceDir $name
            # The archive takes its name only when it matches its pin, so a wrong one never sits in the folder as if it were the right one.
            $partial = "$target.part"
            Write-Host "==> $($source.Repository) at $($source.Tag)"
            Invoke-WebRequest -Uri "$($source.Repository)/archive/refs/tags/$($source.Tag).tar.gz" -OutFile $partial -MaximumRetryCount 3 -RetryIntervalSec 2
            $actual = (Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant()
            '{0}  {1}' -f $actual, $name
            if (-not $source.Sha256) {
                $problems += "$name has no pinned hash in build/git-manifest.json; if this download is the right one, pin $actual."
                Remove-Item -LiteralPath $partial -Force
            }
            elseif ($source.Sha256 -ne $actual) {
                $problems += "$name is not the archive build/git-manifest.json pins: expected $($source.Sha256), got $actual."
                Remove-Item -LiteralPath $partial -Force
            }
            else {
                Move-Item -LiteralPath $partial -Destination $target -Force
            }
        }
        # The packages of the Windows git bundle go in a folder of their own, so the three tag archives stay easy to find.
        $packageDir = Join-Path $sourceDir 'packages'
        New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
        $packageArchives = @(Get-PackageSourceArchives -ManifestPath (Join-Path $repoRoot 'build/msys2-sources.json'))
        # An older list's archives would otherwise stay in the folder and be published with this release.
        foreach ($name in @(Remove-StalePackageSources -Directory $packageDir -Archives $packageArchives)) {
            Write-Host "removed stale source archive: $name"
        }
        foreach ($package in $packageArchives) {
            $target = Join-Path $packageDir $package.File
            $partial = "$target.part"
            Write-Host "==> $($package.File)"
            # An archive already in the folder was checked against its pin when it took its name, so only a changed pin makes it download again.
            if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -eq $package.Sha256) {
                '{0}  {1}' -f $package.Sha256, $package.File
                continue
            }
            Invoke-WebRequest -Uri $package.Url -OutFile $partial -MaximumRetryCount 3 -RetryIntervalSec 2
            $actual = (Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant()
            '{0}  {1}' -f $actual, $package.File
            if (-not $package.Sha256) {
                $problems += "$($package.File) has no pinned hash in build/msys2-sources.json; if this download is the right one, pin $actual."
                Remove-Item -LiteralPath $partial -Force
            }
            elseif ($package.Sha256 -ne $actual) {
                $problems += "$($package.File) is not the archive build/msys2-sources.json pins: expected $($package.Sha256), got $actual."
                Remove-Item -LiteralPath $partial -Force
            }
            else {
                Move-Item -LiteralPath $partial -Destination $target -Force
            }
        }
        if ($problems.Count -gt 0) { throw ($problems -join [Environment]::NewLine) }
    }

    Invoke-Step 'restore tools' { dotnet tool restore }

    if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
    Invoke-Step 'publish' {
        dotnet publish src/Savepoint.App -c Release -r $rid --self-contained -o $publishDir "-m:$buildNodes" -nodeReuse:false -p:UseSharedCompilation=false "-p:SourceRevisionId=$revision"
    }

    Write-Host '==> check the publish folder'
    foreach ($expected in 'SavePoint.exe', 'THIRD-PARTY-NOTICES.md', 'LICENSE', 'git/.savepoint-git-stamp') {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDir $expected) -PathType Leaf)) {
            throw "$expected is missing from $publishDir."
        }
    }
    # The stamp inside the bundle names the git executable, relative to git/.
    $entry = ((Get-Content -LiteralPath (Join-Path $publishDir 'git/.savepoint-git-stamp') | Where-Object { $_ -like 'entry=*' }) -replace '^entry=', '').Trim()
    if (-not $entry -or -not (Test-Path -LiteralPath (Join-Path $publishDir "git/$entry") -PathType Leaf)) {
        throw "The git executable ($entry) is missing from $publishDir/git."
    }
    $licenses = Join-Path $publishDir 'licenses'
    if (-not (Test-Path -LiteralPath $licenses -PathType Container) -or -not (Get-ChildItem -LiteralPath $licenses -File)) {
        throw "licenses/ is missing or empty in $publishDir."
    }

    # A release is repacked from scratch: old packages would otherwise be taken for delta bases.
    if (Test-Path -LiteralPath $releaseDir) { Remove-Item -LiteralPath $releaseDir -Recurse -Force }
    # --exclude drops the debug symbols and the XML documentation files the build writes next to the assemblies. Giving it replaces vpk's default (.pdb), so the pattern repeats that.
    Invoke-Step 'pack' {
        dotnet vpk --skip-updates pack --packId $packId --packVersion $version --packTitle SavePoint --packDir $publishDir --mainExe SavePoint.exe --icon $icon --runtime $rid --shortcuts StartMenuRoot --exclude '(?i)\.pdb$|(^|[/\\])savepoint(\.core)?\.xml$' --outputDir $releaseDir
    }
    # vpk names the files after the pack id; the ones people download carry the app's name.
    foreach ($suffix in '-win-Setup.exe', '-win-Portable.zip') {
        Move-Item -LiteralPath (Join-Path $releaseDir "$packId$suffix") -Destination (Join-Path $releaseDir "SavePoint$suffix") -Force
    }

    Write-Host "==> packages in $releaseDir"
    Get-ChildItem -LiteralPath $releaseDir -File | ForEach-Object { '{0,14:N0} bytes  {1}' -f $_.Length, $_.Name }

    # The setup is an installer around the full package, so the portable zip and the full package are what can be opened.
    Write-Host '==> check the packages'
    $packages = @(Get-ChildItem -LiteralPath $releaseDir -File | Where-Object { $_.Name -like '*.zip' -or $_.Name -like '*-full.nupkg' })
    if ($packages.Count -eq 0) { throw "vpk wrote no zip or full package in $releaseDir to look inside." }
    foreach ($package in $packages) {
        Assert-PackageHolds -Path $package.FullName -ListingDir (Join-Path $repoRoot "artifacts/release/contents-$rid")
    }
}
catch {
    Write-Host "package failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host 'package done' -ForegroundColor Green
