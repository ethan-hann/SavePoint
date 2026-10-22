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
Also downloads the source archives of the three git tags THIRD-PARTY-NOTICES.md names into
artifacts/release/source and prints their SHA-256, for publishing next to the release.
#>
[CmdletBinding()]
param(
    [switch]$Sources
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

function Get-SourceTags {
    # The tags the notices promise source for: "<repository URL>, tag `<tag>`".
    $text = Get-Content -LiteralPath (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Raw
    $found = [regex]::Matches($text, '(https://github\.com/[\w.-]+/[\w.-]+), tag `([^`]+)`')
    if ($found.Count -ne 3) {
        throw "THIRD-PARTY-NOTICES.md names $($found.Count) source tags; expected 3 (git, Git for Windows, dugite-native)."
    }
    foreach ($match in $found) {
        [pscustomobject]@{ Repository = $match.Groups[1].Value; Tag = $match.Groups[2].Value }
    }
}

try {
    $props = Join-Path $repoRoot 'Directory.Build.props'
    $version = (Select-Xml -Path $props -XPath '//Version').Node.InnerText.Trim()
    if (-not $version) { throw 'Directory.Build.props has no <Version>.' }
    # The setup installs into %LOCALAPPDATA%\<pack id> and deletes that whole folder on uninstall, so it must not be SavePoint's data folder (D38).
    $packId = (Select-Xml -Path $props -XPath '//SavepointPackId').Node.InnerText.Trim()
    if (-not $packId) { throw 'Directory.Build.props has no <SavepointPackId>.' }
    Write-Host "==> SavePoint $version for $rid"

    Invoke-Step 'restore tools' { dotnet tool restore }

    if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
    Invoke-Step 'publish' {
        dotnet publish src/Savepoint.App -c Release -r $rid --self-contained -o $publishDir "-m:$buildNodes" -nodeReuse:false -p:UseSharedCompilation=false
    }

    Write-Host '==> check the publish folder'
    foreach ($expected in 'SavePoint.exe', 'THIRD-PARTY-NOTICES.md', 'git/.savepoint-git-stamp') {
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

    if ($Sources) {
        $sourceDir = Join-Path $repoRoot 'artifacts/release/source'
        New-Item -ItemType Directory -Force -Path $sourceDir | Out-Null
        $ProgressPreference = 'SilentlyContinue'
        foreach ($source in Get-SourceTags) {
            $name = ($source.Repository -replace '^https://github\.com/', '' -replace '/', '-') + "-$($source.Tag).tar.gz"
            $target = Join-Path $sourceDir $name
            Write-Host "==> $($source.Repository) at $($source.Tag)"
            Invoke-WebRequest -Uri "$($source.Repository)/archive/refs/tags/$($source.Tag).tar.gz" -OutFile $target
            '{0}  {1}' -f (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant(), $name
        }
    }
}
catch {
    Write-Host "package failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host 'package done' -ForegroundColor Green
