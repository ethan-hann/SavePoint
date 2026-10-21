#Requires -Version 7
# Local gate: build, test, format check. Exits non-zero on the first failure.
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

Set-Location (Split-Path -Parent $PSScriptRoot)

function Invoke-Step {
    param([string]$Name, [scriptblock]$Command)
    Write-Host "==> $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

try {
    Invoke-Step 'build' { dotnet build Savepoint.slnx -c Release -warnaserror }
    Invoke-Step 'test' { dotnet test Savepoint.slnx -c Release --no-build }
    Invoke-Step 'format' { dotnet format Savepoint.slnx --verify-no-changes }
}
catch {
    Write-Host "gate failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host 'gate passed' -ForegroundColor Green
