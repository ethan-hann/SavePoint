#Requires -Version 7
<#
.SYNOPSIS
Runs the gate on Linux under WSL, from a dedicated worktree so Linux and Windows bin/obj never mix.

.DESCRIPTION
Creates <repo>/.linux-gate as a detached worktree when it is missing, checks the commit out in it
(both are done here because Linux git cannot follow a worktree file that holds Windows paths), then
calls build/gate-linux.sh inside WSL and returns its exit code.

.PARAMETER Commit
What to gate: a branch, tag or commit. Defaults to main. It is resolved against this checkout, so
HEAD means this checkout's HEAD, which lets a branch be gated before it is merged.

.PARAMETER Distro
The WSL distribution to run in.
#>
[CmdletBinding()]
param(
    [string]$Commit = 'main',
    [string]$Distro = 'Ubuntu'
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

    $script = "$(ConvertTo-WslPath $repoRoot)/build/gate-linux.sh"
    Write-Host "==> wsl -d $Distro bash $script"
    & wsl.exe -d $Distro -- bash $script
    $code = $LASTEXITCODE
}
catch {
    Write-Host "linux gate failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

if ($code -ne 0) {
    Write-Host "linux gate failed with exit code $code" -ForegroundColor Red
    exit $code
}

Write-Host 'linux gate passed' -ForegroundColor Green
