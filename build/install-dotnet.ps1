# Installs the newest .NET 10 SDK into the given folder, for CI jobs. Microsoft's channel link can name a release before its
# files can be downloaded (the origin answers 409), so when the channel install fails, the SDK the release feed lists as the
# latest is installed instead. -Channel replaces the channel, which is how the fallback is tried by hand.
param(
    [Parameter(Mandatory)][string]$InstallDir,
    [string]$Channel = '10.0'
)

$ErrorActionPreference = 'Stop'
$script = Join-Path ([IO.Path]::GetTempPath()) "dotnet-install-$PID.ps1"
try {
    Invoke-WebRequest -UseBasicParsing -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile $script -MaximumRetryCount 3 -RetryIntervalSec 2
    try {
        & $script -Channel $Channel -InstallDir $InstallDir -NoPath
        return
    } catch {
        Write-Host "The channel install failed: $($_.Exception.Message)"
    }

    $feed = Invoke-RestMethod -Uri https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json -MaximumRetryCount 3 -RetryIntervalSec 2
    $latest = $feed.'latest-sdk'
    Write-Host "The channel's newest SDK could not be downloaded; installing $latest, the latest the release feed lists."
    & $script -Version $latest -InstallDir $InstallDir -NoPath
} finally {
    Remove-Item -Force -ErrorAction SilentlyContinue $script
}
