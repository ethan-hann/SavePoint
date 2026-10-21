#Requires -Version 7
# Local gate: build, test, format check. Exits non-zero on the first failure.
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

Set-Location (Split-Path -Parent $PSScriptRoot)

# Builds, test hosts and git all inherit this, so a gate never starves the desktop it runs on.
[System.Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'

# Caps that keep one gate from saturating the machine; override per run when needed.
$buildNodes = if ($env:SAVEPOINT_GATE_BUILD_NODES) { $env:SAVEPOINT_GATE_BUILD_NODES } else { '4' }
$testThreads = if ($env:SAVEPOINT_GATE_TEST_THREADS) { $env:SAVEPOINT_GATE_TEST_THREADS } else { '8' }

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
    Invoke-Step 'test' { dotnet test Savepoint.slnx -c Release --no-build --max-parallel-test-modules 1 --max-threads $testThreads }
    Invoke-Step 'format' { dotnet format Savepoint.slnx --verify-no-changes }
}
catch {
    Write-Host "gate failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    $lock.Dispose()
}

Write-Host 'gate passed' -ForegroundColor Green
