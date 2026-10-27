#Requires -Version 7
<#
.SYNOPSIS
Builds the Linux (linux-x64) package on the Linux test VM over SSH and copies it back.

.DESCRIPTION
The VM is a real Ubuntu on ext4, which is also what builds the package in CI. The commit is exported with git archive, uploaded
with build/package-linux.sh, and built by build/package.sh in a fixed folder on the VM (default ~/package), where bin, obj, the
SDK and the NuGet caches survive between runs. That folder is never the gate's (~/gate), so a package run and a gate run never
share a tree. Nothing is pushed to Forgejo, so a local branch can be packaged. Only committed files are packaged; uncommitted
changes in this checkout are not sent, so the build is always tied to the commit it names.

The package (the AppImage and the files vpk writes beside it) lands in <repo>/artifacts/release/linux-x64, the listing of its
contents in <repo>/artifacts/release/contents-linux-x64.txt, and with -Sources the source archives in
<repo>/artifacts/release/source. The run's log is copied to .tools/package-linux-last.log (or -LogPath), also when it fails.

One package run happens on the VM at a time, and none while a gate (build/gate-linux.ps1) runs there, because the two together
starve tests that depend on timing. Such a run is refused at once, with exit code 75, and changes nothing there. When a run ends, the
VM's old SDKs and runtimes and the NuGet packages unread for 30 days are removed from the package folder.
Key authentication only: BatchMode is on, so a missing key fails instead of asking for a password.

.PARAMETER Commit
What to package: a branch, tag or commit, resolved in this checkout. Defaults to HEAD.

.PARAMETER Sources
Also downloads and checks the source archives, as build/package.sh --sources does.

.PARAMETER VmHost
The VM's address. Environment variable SAVEPOINT_VM_HOST; default 192.168.233.131.

.PARAMETER VmUser
The VM account. Environment variable SAVEPOINT_VM_USER; default testbench.

.PARAMETER VmKey
The private key file. Environment variable SAVEPOINT_VM_KEY; default ~/.ssh/savepoint-vm_ed25519.

.PARAMETER VmDir
The package folder on the VM, relative to the account's home. Environment variable SAVEPOINT_VM_PACKAGE_DIR; default package.
The gate's folder (SAVEPOINT_VM_DIR, default gate) is refused.

.PARAMETER LogPath
Where the log is copied to. Defaults to .tools/package-linux-last.log under this checkout.
#>
[CmdletBinding()]
param(
    [string]$Commit = 'HEAD',
    [switch]$Sources,
    [string]$VmHost = $(if ($env:SAVEPOINT_VM_HOST) { $env:SAVEPOINT_VM_HOST } else { '192.168.233.131' }),
    [string]$VmUser = $(if ($env:SAVEPOINT_VM_USER) { $env:SAVEPOINT_VM_USER } else { 'testbench' }),
    [string]$VmKey = $(if ($env:SAVEPOINT_VM_KEY) { $env:SAVEPOINT_VM_KEY } else { Join-Path $HOME '.ssh/savepoint-vm_ed25519' }),
    [string]$VmDir = $(if ($env:SAVEPOINT_VM_PACKAGE_DIR) { $env:SAVEPOINT_VM_PACKAGE_DIR } else { 'package' }),
    [string]$LogPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $LogPath) { $LogPath = Join-Path $repoRoot '.tools/package-linux-last.log' }

function Invoke-Git {
    $output = & git -C $repoRoot @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed with exit code $LASTEXITCODE" }
    return $output
}

# Quotes a value for the shell file that run.env is, so no character in it means anything to the VM's shell.
function ConvertTo-ShellWord([string]$Value) {
    return "'" + $Value.Replace("'", "'\''") + "'"
}

if (-not (Test-Path -LiteralPath $VmKey)) { throw "The SSH key $VmKey does not exist. Set SAVEPOINT_VM_KEY or pass -VmKey." }
if ($VmDir -notmatch '^[A-Za-z0-9_./-]+$' -or $VmDir.Contains('..')) { throw "VmDir may hold only letters, digits and . _ / - : $VmDir" }
$gateDir = if ($env:SAVEPOINT_VM_DIR) { $env:SAVEPOINT_VM_DIR } else { 'gate' }
if ($VmDir.Trim('/') -eq $gateDir.Trim('/')) { throw "VmDir $VmDir is the gate's folder; a package run needs one of its own." }

$sshOptions = @(
    '-i', $VmKey,
    '-o', 'BatchMode=yes',
    '-o', 'IdentitiesOnly=yes',
    '-o', 'StrictHostKeyChecking=accept-new',
    '-o', 'ConnectTimeout=15',
    '-o', 'ServerAliveInterval=30'
)
$target = "$VmUser@$VmHost"

$sha = (Invoke-Git rev-parse --verify "$Commit^{commit}" | Select-Object -First 1).Trim()
$id = "$($sha.Substring(0, 12))-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"

$dirty = @(Invoke-Git status --porcelain)
if ($dirty.Count -gt 0) {
    Write-Host "==> NOTE: this checkout has uncommitted changes. Only the committed $($sha.Substring(0, 12)) is packaged." -ForegroundColor Yellow
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) "savepoint-package-$id"
$upload = Join-Path $work $id
$fetched = Join-Path $work 'fetched'
New-Item -ItemType Directory -Force -Path $upload, $fetched | Out-Null
$remoteUpload = "$VmDir/incoming/$id"
$started = $false
$code = 1
$clock = [System.Diagnostics.Stopwatch]::StartNew()
try {
    Write-Host "==> exporting $Commit ($($sha.Substring(0, 12)))"
    Invoke-Git -c core.autocrlf=false archive --format=tar.gz -o (Join-Path $upload 'commit.tar.gz') $sha | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'package-linux.sh') -Destination (Join-Path $upload 'package-linux.sh')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'vm-common.sh') -Destination (Join-Path $upload 'vm-common.sh')

    $settings = [ordered]@{
        PACKAGE_ID      = $id
        PACKAGE_COMMIT  = $sha
        PACKAGE_DIR     = $VmDir
        PACKAGE_SOURCES = $(if ($Sources) { '1' } else { '0' })
    }
    $lines = foreach ($key in $settings.Keys) { "$key=$(ConvertTo-ShellWord ([string]$settings[$key]))" }
    # A file written here is run.env for the VM's shell: LF endings, no byte order mark.
    [System.IO.File]::WriteAllText((Join-Path $upload 'run.env'), (($lines -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))

    Write-Host "==> uploading to $target`:$remoteUpload"
    & ssh @sshOptions $target "mkdir -p $VmDir/incoming"
    if ($LASTEXITCODE -ne 0) { throw "Cannot reach $target over SSH with key authentication (exit code $LASTEXITCODE)." }
    & scp @sshOptions -q -r $upload "${target}:$VmDir/incoming/"
    if ($LASTEXITCODE -ne 0) { throw "The upload failed (scp exit code $LASTEXITCODE)." }

    Write-Host "==> package.sh on $target"
    $started = $true
    & ssh @sshOptions $target "bash $remoteUpload/package-linux.sh $remoteUpload"
    $code = $LASTEXITCODE

    # ssh itself failing (255) or being killed means the connection ended, not the build; the run may still be going.
    if ($code -eq 255 -or $code -lt 0) {
        Write-Host '==> the connection ended before the package did: stopping the run on the VM' -ForegroundColor Yellow
        & ssh @sshOptions $target "kill -TERM `$(cat $VmDir/run-$id.pid)" 2>$null
    }

    if ($code -eq 75) {
        Write-Host 'linux package refused: the VM is busy with another package build or a gate (see above).' -ForegroundColor Red
    }
    else {
        $logDir = Split-Path -Parent $LogPath
        if ($logDir) { New-Item -ItemType Directory -Force -Path $logDir | Out-Null }
        & scp @sshOptions -q "${target}:$VmDir/logs/$id.log" $LogPath
        if ($LASTEXITCODE -eq 0) { Write-Host "==> log: $LogPath" } else { Write-Host "==> the log could not be copied back from $VmDir/logs/$id.log" -ForegroundColor Yellow }
    }
    $started = $false

    if ($code -eq 0) {
        Write-Host '==> copying the package back'
        & scp @sshOptions -q -r "${target}:$VmDir/out/$id/release" $fetched
        if ($LASTEXITCODE -ne 0) { throw "The package could not be copied back (scp exit code $LASTEXITCODE)." }
        $release = Join-Path $repoRoot 'artifacts/release'
        New-Item -ItemType Directory -Force -Path $release | Out-Null
        # A package is repacked from scratch, so an older one never stays beside the new one.
        Remove-Item -LiteralPath (Join-Path $release 'linux-x64') -Recurse -Force -ErrorAction SilentlyContinue
        Copy-Item -Path (Join-Path $fetched 'release/*') -Destination $release -Recurse -Force
        $appImage = Join-Path $release 'linux-x64/SavePoint.AppImage'
        if (-not (Test-Path -LiteralPath $appImage)) { throw "The VM's package holds no SavePoint.AppImage; expected $appImage." }
    }
}
catch {
    Write-Host "linux package failed: $($_.Exception.Message)" -ForegroundColor Red
    $code = 1
}
finally {
    # An interrupted run would otherwise go on holding the VM: ask its script to stop the whole session.
    if ($started) {
        Write-Host '==> interrupted: stopping the package run on the VM' -ForegroundColor Yellow
        & ssh @sshOptions $target "kill -TERM `$(cat $VmDir/run-$id.pid)" 2>$null
    }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($code -ne 0) {
    Write-Host "linux package failed with exit code $code" -ForegroundColor Red
    exit $code
}

$seconds = [int]$clock.Elapsed.TotalSeconds
Write-Host "linux package done in ${seconds}s: $(Join-Path $repoRoot 'artifacts/release/linux-x64')" -ForegroundColor Green
