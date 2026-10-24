#Requires -Version 7
# Local gate: build, test, format check. Exits non-zero on the first failure.
#
# SAVEPOINT_TEST_ARGS takes only a report (--report-*) and retries of failed tests (--retry-failed-tests*). Anything else, such
# as a filter, is refused unless -AllowNarrowedTests says it is on purpose, because a run that tests part of the code or lets a
# failure pass would read as a passed gate.
[CmdletBinding()]
param(
    [switch]$AllowNarrowedTests
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

Set-Location (Split-Path -Parent $PSScriptRoot)

# Builds, test hosts and git all inherit this, so a gate never starves the desktop it runs on.
[System.Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'
# No telemetry from the .NET CLI (D32); Avalonia's build telemetry is disabled in Directory.Build.targets.
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

# Caps that keep one gate from saturating the machine; override per run when needed.
$buildNodes = if ($env:SAVEPOINT_GATE_BUILD_NODES) { $env:SAVEPOINT_GATE_BUILD_NODES } else { '4' }
$testThreads = if ($env:SAVEPOINT_GATE_TEST_THREADS) { $env:SAVEPOINT_GATE_TEST_THREADS } else { '4' }
# Extra test-platform arguments (a report, retries), space-separated, as gate.sh takes them.
[string[]]$testArgs = if ($env:SAVEPOINT_TEST_ARGS) { @($env:SAVEPOINT_TEST_ARGS -split '\s+' | Where-Object { $_ }) } else { @() }

# Says which of the arguments the gate does not take. Only a report and retries of failed tests are allowed; any other argument, a
# filter in whatever spelling, a response file (@file) or an option that lets failures pass, could make "gate passed" mean less
# than a full run. A word without dashes is allowed only as the value of an allowed option before it. Matching is case sensitive,
# so an odd spelling is refused, never let through.
function Get-NarrowingArguments {
    param([string[]]$Arguments)
    $afterAllowed = $false
    foreach ($argument in $Arguments) {
        if ($argument -cmatch '^--(report-[A-Za-z0-9-]+|retry-failed-tests(-[A-Za-z0-9-]+)?)(=.*)?$') {
            $afterAllowed = -not $argument.Contains('=')
        }
        elseif ($afterAllowed -and $argument -notmatch '^[-@]') {
            $afterAllowed = $false
        }
        else {
            $afterAllowed = $false
            $argument
        }
    }
}

# Said first, so a leftover value in the shell is seen before anything runs.
$narrowing = @()
if ($testArgs.Count -gt 0) {
    Write-Host "==> extra test arguments (SAVEPOINT_TEST_ARGS): $($testArgs -join ' ')"
    $narrowing = @(Get-NarrowingArguments $testArgs)
    if ($narrowing.Count -gt 0 -and -not $AllowNarrowedTests) {
        Write-Host "gate refused: SAVEPOINT_TEST_ARGS holds $($narrowing -join ' '), which could run only some of the tests or let a failure pass. Only a report and retries are allowed there. Unset it, or pass -AllowNarrowedTests if that is on purpose." -ForegroundColor Red
        exit 1
    }
    if ($narrowing.Count -gt 0) {
        Write-Host '==> NARROWED on purpose: the extra arguments can leave tests out or let a failure pass, so a pass is not a full gate.' -ForegroundColor Yellow
    }
}

# One gate at a time across every worktree of this repository: they share one lock file in the main checkout.
$commonGitDir = git rev-parse --path-format=absolute --git-common-dir
$lockDir = Join-Path (Split-Path -Parent $commonGitDir) '.tools/locks'
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
            Write-Host "==> waiting for another gate to finish ($lockPath)"
            $waited = $true
        }
        Start-Sleep -Seconds 5
    }
}

function Invoke-Step {
    param([string]$Name, [scriptblock]$Command)
    Write-Host "==> $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

try {
    Invoke-Step 'build' { dotnet build Savepoint.slnx -c Release -warnaserror "-m:$buildNodes" -nodeReuse:false }
    Invoke-Step 'test' { dotnet test Savepoint.slnx -c Release --no-build --max-parallel-test-modules 2 --max-threads $testThreads @testArgs }
    Invoke-Step 'format' { dotnet format Savepoint.slnx --verify-no-changes }
}
catch {
    Write-Host "gate failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    $lock.Dispose()
}

if ($narrowing.Count -gt 0) {
    Write-Host 'gate passed, NARROWED: not a full gate' -ForegroundColor Yellow
}
else {
    Write-Host 'gate passed' -ForegroundColor Green
}
