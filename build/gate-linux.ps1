#Requires -Version 7
<#
.SYNOPSIS
Runs the gate (build/gate.sh) on the Linux test VM over SSH and returns its exit code.

.DESCRIPTION
The VM is a real Ubuntu on ext4, so the run sees case-sensitive names, permissions and Unix sockets, which a Windows drive under
WSL does not. The commit is exported with git archive, uploaded with build/gate-linux.sh, and built and tested in a fixed folder
on the VM (default ~/gate), where bin and obj survive between runs. Nothing is pushed to Forgejo, so a local branch can be gated.
Only committed files are gated; uncommitted changes in this checkout are not sent.

One gate runs on the VM at a time, and none while a package build (build/package-linux.ps1) runs there, because the two together
starve tests that depend on timing. Such a run is refused at once, with exit code 75, and changes nothing there. When a run ends, the
VM's old SDKs and runtimes and the NuGet packages unread for 30 days are removed from the gate's folder.
The run's log is copied back to .tools/gate-linux-last.log (or -LogPath), also when the gate fails.
Key authentication only: BatchMode is on, so a missing key fails instead of asking for a password.

.PARAMETER Commit
What to gate: a branch, tag or commit, resolved in this checkout. Defaults to HEAD.

.PARAMETER VmHost
The VM's address. Environment variable SAVEPOINT_VM_HOST; default 192.168.233.131.

.PARAMETER VmUser
The VM account. Environment variable SAVEPOINT_VM_USER; default testbench.

.PARAMETER VmKey
The private key file. Environment variable SAVEPOINT_VM_KEY; default ~/.ssh/savepoint-vm_ed25519.

.PARAMETER VmDir
The gate folder on the VM, relative to the account's home. Environment variable SAVEPOINT_VM_DIR; default gate.

.PARAMETER TestArgs
Extra test arguments, as SAVEPOINT_TEST_ARGS takes them on gate.sh. Defaults to the SAVEPOINT_TEST_ARGS environment variable.

.PARAMETER AllowNarrowedTests
Allows TestArgs that narrow the run (a filter); the result is then not a full gate.

.PARAMETER LogPath
Where the log is copied to. Defaults to .tools/gate-linux-last.log under this checkout.
#>
[CmdletBinding()]
param(
    [string]$Commit = 'HEAD',
    [string]$VmHost = $(if ($env:SAVEPOINT_VM_HOST) { $env:SAVEPOINT_VM_HOST } else { '192.168.233.131' }),
    [string]$VmUser = $(if ($env:SAVEPOINT_VM_USER) { $env:SAVEPOINT_VM_USER } else { 'testbench' }),
    [string]$VmKey = $(if ($env:SAVEPOINT_VM_KEY) { $env:SAVEPOINT_VM_KEY } else { Join-Path $HOME '.ssh/savepoint-vm_ed25519' }),
    [string]$VmDir = $(if ($env:SAVEPOINT_VM_DIR) { $env:SAVEPOINT_VM_DIR } else { 'gate' }),
    [string]$TestArgs = $env:SAVEPOINT_TEST_ARGS,
    [switch]$AllowNarrowedTests,
    [string]$LogPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $LogPath) { $LogPath = Join-Path $repoRoot '.tools/gate-linux-last.log' }

function Invoke-Git {
    $output = & git -C $repoRoot @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed with exit code $LASTEXITCODE" }
    return $output
}

# Quotes a value for the shell file that run.env is, so no character in it means anything to the VM's shell.
function ConvertTo-ShellWord([string]$Value) {
    return "'" + $Value.Replace("'", "'\''") + "'"
}

# True when the gate's log says the run was narrowed: the last line build/gate.sh prints, which the VM script copies as it is.
function Test-GateNarrowed([string]$Log) {
    return [bool](Select-String -LiteralPath $Log -Pattern '^gate passed, NARROWED' -Quiet)
}

if (-not (Test-Path -LiteralPath $VmKey)) { throw "The SSH key $VmKey does not exist. Set SAVEPOINT_VM_KEY or pass -VmKey." }
if ($VmDir -notmatch '^[A-Za-z0-9_./-]+$' -or $VmDir.Contains('..')) { throw "VmDir may hold only letters, digits and . _ / - : $VmDir" }

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
    Write-Host "==> NOTE: this checkout has uncommitted changes. Only the committed $($sha.Substring(0, 12)) is gated." -ForegroundColor Yellow
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) "savepoint-gate-$id"
$upload = Join-Path $work $id
New-Item -ItemType Directory -Force -Path $upload | Out-Null
$remoteUpload = "$VmDir/incoming/$id"
$started = $false
$logCopied = $false
$code = 1
try {
    Write-Host "==> exporting $Commit ($($sha.Substring(0, 12)))"
    Invoke-Git -c core.autocrlf=false archive --format=tar.gz -o (Join-Path $upload 'commit.tar.gz') $sha | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'gate-linux.sh') -Destination (Join-Path $upload 'gate-linux.sh')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'vm-common.sh') -Destination (Join-Path $upload 'vm-common.sh')

    $settings = [ordered]@{
        GATE_ID             = $id
        GATE_COMMIT         = $sha
        GATE_DIR            = $VmDir
        GATE_ALLOW_NARROWED = $(if ($AllowNarrowedTests) { '1' } else { '0' })
        SAVEPOINT_TEST_ARGS = $TestArgs
    }
    # The caps gate.sh reads; unset means its own defaults.
    foreach ($name in 'SAVEPOINT_GATE_BUILD_NODES', 'SAVEPOINT_GATE_TEST_THREADS') {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($value) { $settings[$name] = $value }
    }
    $lines = foreach ($key in $settings.Keys) { "$key=$(ConvertTo-ShellWord ([string]$settings[$key]))" }
    # A file written here is run.env for the VM's shell: LF endings, no byte order mark.
    [System.IO.File]::WriteAllText((Join-Path $upload 'run.env'), (($lines -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))

    Write-Host "==> uploading to $target`:$remoteUpload"
    & ssh @sshOptions $target "mkdir -p $VmDir/incoming"
    if ($LASTEXITCODE -ne 0) { throw "Cannot reach $target over SSH with key authentication (exit code $LASTEXITCODE)." }
    & scp @sshOptions -q -r $upload "${target}:$VmDir/incoming/"
    if ($LASTEXITCODE -ne 0) { throw "The upload failed (scp exit code $LASTEXITCODE)." }

    Write-Host "==> gate.sh on $target"
    $started = $true
    & ssh @sshOptions $target "bash $remoteUpload/gate-linux.sh $remoteUpload"
    $code = $LASTEXITCODE

    # ssh itself failing (255) or being killed means the connection ended, not the gate; the run may still be going.
    if ($code -eq 255 -or $code -lt 0) {
        Write-Host '==> the connection ended before the gate did: stopping the gate on the VM' -ForegroundColor Yellow
        & ssh @sshOptions $target "kill -TERM `$(cat $VmDir/run-$id.pid)" 2>$null
    }

    if ($code -eq 75) {
        Write-Host 'linux gate refused: the VM is busy with another gate or a package build (see above).' -ForegroundColor Red
    }
    else {
        $logDir = Split-Path -Parent $LogPath
        if ($logDir) { New-Item -ItemType Directory -Force -Path $logDir | Out-Null }
        & scp @sshOptions -q "${target}:$VmDir/logs/$id.log" $LogPath
        if ($LASTEXITCODE -eq 0) { $logCopied = $true; Write-Host "==> log: $LogPath" } else { Write-Host "==> the log could not be copied back from $VmDir/logs/$id.log" -ForegroundColor Yellow }
    }
    $started = $false
}
catch {
    Write-Host "linux gate failed: $($_.Exception.Message)" -ForegroundColor Red
    $code = 1
}
finally {
    # An interrupted run would otherwise go on holding the VM: ask its script to stop the whole session.
    if ($started) {
        Write-Host '==> interrupted: stopping the gate on the VM' -ForegroundColor Yellow
        & ssh @sshOptions $target "kill -TERM `$(cat $VmDir/run-$id.pid)" 2>$null
    }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($code -ne 0) {
    Write-Host "linux gate failed with exit code $code" -ForegroundColor Red
    exit $code
}

# build/gate.sh decides whether the run was narrowed (a report option alone does not narrow it) and says so on its last line.
if ($logCopied) {
    $narrowed = Test-GateNarrowed $LogPath
}
else {
    # Without the log the safe reading is the wider one.
    $narrowed = [bool]($AllowNarrowedTests -and $TestArgs)
}

if ($narrowed) {
    Write-Host 'linux gate passed, NARROWED: not a full gate' -ForegroundColor Yellow
}
else {
    Write-Host 'linux gate passed' -ForegroundColor Green
}
