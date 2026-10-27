#Requires -Version 7
<#
.SYNOPSIS
Builds the Linux (linux-x64) package from Windows under WSL, in the dedicated Linux-only worktree, .linux-gate.

.DESCRIPTION
Creates <repo>/.linux-gate as a detached worktree when it is missing and checks the commit out in it (both are done here
because Linux git cannot follow a worktree file that holds Windows paths). Windows git then says which commit that is and
whether the worktree is clean, and build/package.sh runs inside WSL from that worktree with the answer passed in as
--revision, so the script never asks Linux git. The SDK and caches are the main checkout's .tools. The package lands in .linux-gate/artifacts/release/linux-x64.

It takes the machine-wide gate lock first, so it never overlaps a Windows gate.

.PARAMETER Commit
What to package: a branch, tag or commit. Defaults to HEAD, this checkout's HEAD, like build/package.ps1, which packs the
commit it is run on.

.PARAMETER Distro
The WSL distribution to run in.

.PARAMETER Sources
Also downloads and checks the source archives, as build/package.sh --sources does.

.PARAMETER AllowDirty
Packs a worktree that is not clean anyway, for a trial package. The build is then marked "-dirty". Without it the run is
refused, because a package from such a tree cannot be tied to a commit.
#>
[CmdletBinding()]
param(
    [string]$Commit = 'HEAD',
    [string]$Distro = 'Ubuntu',
    [switch]$Sources,
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$gateTree = Join-Path $repoRoot '.linux-gate'

function Invoke-Git {
    $output = & git -C $repoRoot @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed with exit code $LASTEXITCODE" }
    return $output
}

function ConvertTo-WslPath([string]$Path) {
    $full = [System.IO.Path]::GetFullPath($Path)
    if ($full -notmatch '^([A-Za-z]):\\(.*)$') { throw "WSL can only be given a path on a drive letter: $full" }
    return "/mnt/$($Matches[1].ToLowerInvariant())/$($Matches[2].Replace('\', '/'))"
}

# Shares the machine-wide gate lock with build/gate.ps1, so a package build never overlaps a gate.
$commonGitDir = (Invoke-Git rev-parse --path-format=absolute --git-common-dir | Select-Object -First 1).Trim()
$mainCheckout = Split-Path -Parent $commonGitDir
$lockDir = Join-Path $mainCheckout '.tools/locks'
New-Item -ItemType Directory -Force -Path $lockDir | Out-Null
$lockPath = Join-Path $lockDir 'gate.lock'
$lock = $null
$waited = $false
while ($null -eq $lock) {
    try {
        $lock = [System.IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None')
    }
    catch [System.IO.IOException] {
        if (-not $waited) {
            Write-Host "==> waiting for another gate or package to finish ($lockPath)"
            $waited = $true
        }
        Start-Sleep -Seconds 5
    }
}

try {
    $sha = (Invoke-Git rev-parse --verify "$Commit^{commit}" | Select-Object -First 1).Trim()

    if (-not (Test-Path -LiteralPath (Join-Path $gateTree '.git'))) {
        if (Test-Path -LiteralPath $gateTree) {
            throw "$gateTree exists but is not a worktree. Delete it and run this again."
        }
        Write-Host "==> creating the worktree $gateTree"
        Invoke-Git worktree add --detach $gateTree $sha | Out-Host
    }

    Write-Host "==> checking out $Commit ($($sha.Substring(0, 12))) in $gateTree"
    & git -C $gateTree checkout --detach --force $sha
    if ($LASTEXITCODE -ne 0) { throw "checkout failed with exit code $LASTEXITCODE" }

    # Windows git answers both questions, since Linux git cannot read this worktree. A failing git is not a clean tree.
    $revision = (& git -C $gateTree rev-parse --short=12 HEAD | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or -not $revision) { throw 'git could not say which commit the worktree is at.' }
    $revision = $revision.Trim()
    $changes = @(& git -C $gateTree status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw 'git could not say whether the worktree has changes.' }
    if ($changes.Count -gt 0) {
        if (-not $AllowDirty) {
            throw "$gateTree has $($changes.Count) uncommitted change(s), so the package could not be tied to a commit. Remove them, or pass -AllowDirty for a trial package."
        }
        $revision = "$revision-dirty"
    }

    $script = "$(ConvertTo-WslPath $gateTree)/build/package.sh"
    $packageArguments = @('--revision', $revision, '--tools-dir', "$(ConvertTo-WslPath $mainCheckout)/.tools")
    if ($Sources) { $packageArguments += '--sources' }
    Write-Host "==> wsl -d $Distro bash $script $($packageArguments -join ' ')"
    & wsl.exe -d $Distro -- bash $script @packageArguments
    $code = $LASTEXITCODE
}
catch {
    Write-Host "linux package failed: $($_.Exception.Message)" -ForegroundColor Red
    $lock.Dispose()
    exit 1
}
$lock.Dispose()

if ($code -ne 0) {
    Write-Host "linux package failed with exit code $code" -ForegroundColor Red
    exit $code
}

Write-Host "linux package done: $(Join-Path $gateTree 'artifacts/release/linux-x64')" -ForegroundColor Green
