#Requires -Version 7
# Local gate: build, test, format check. Exits non-zero on the first failure.
#
# The tests run as four processes at once: the Core tests, and the App tests in the lanes of build/app-test-lanes.txt (the UI tests of
# one process share one UI thread, so they run one at a time). Their test methods must add up to the full listing's, so no test is dropped.
# When less than 10 GB of memory is free (or SAVEPOINT_GATE_APP_LANES=1), the App tests run as one process instead, and the gate says so.
#
# SAVEPOINT_TEST_ARGS takes only a report (--report-*). Anything else, such as a filter or a retry of failed tests (the retry
# extension is no longer part of the test projects), is refused unless -AllowNarrowedTests says it is on purpose, because a run
# that tests part of the code or lets a failure pass would read as a passed gate.
#
# -SkipFormat leaves out the format check. The CI job on Windows passes it because the Linux job runs the same check and the answer
# does not depend on the OS; a local gate keeps the check.
[CmdletBinding()]
param(
    [switch]$AllowNarrowedTests,
    [switch]$SkipFormat
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
# Core is the longest process (git work that needs a thread each), so it gets a third of the machine's logical processors: 4 on a
# small machine, up to 8 on a big one. SAVEPOINT_GATE_TEST_THREADS sets it by hand.
$defaultThreads = [Math]::Min(8, [Math]::Max(4, [int][Math]::Floor([Environment]::ProcessorCount / 3)))
$testThreads = if ($env:SAVEPOINT_GATE_TEST_THREADS) { $env:SAVEPOINT_GATE_TEST_THREADS } else { "$defaultThreads" }
# The App lanes are mostly UI tests that wait for one UI thread each, so two threads serve a lane. At most core + 3 x 2 test threads.
$appThreads = [Math]::Min([int]$testThreads, 2)
# One App process holds the UI tests and the rest, so it gets up to four.
$appSingleThreads = [Math]::Min([int]$testThreads, 4)

# Four test processes at once need about 10 GB free; below that the App tests run as one process, as before the lanes (the
# 7 GB Linux VM ran out of memory with four). SAVEPOINT_GATE_APP_LANES=1 forces one process and 3 forces the lanes (any number
# above 1 does); SAVEPOINT_GATE_AVAILABLE_MEMORY_MB replaces the reading of free memory. On Linux the reading is the smaller of
# MemAvailable and what is left of the container's cgroup limit; SAVEPOINT_GATE_MEMINFO_FILE and SAVEPOINT_GATE_CGROUP_DIR point it at other files
# (the tests use them). When the memory cannot be read at all, the single App process runs.
$lanesMemoryMb = 10240
# Where the test step writes its xunit reports, which the skip check reads. The gate always asks for them, so SAVEPOINT_TEST_ARGS must not.
$resultsDir = Join-Path (Get-Location) 'artifacts/test-results'
# Extra test-platform arguments (a report), space-separated, as gate.sh takes them.
[string[]]$testArgs = if ($env:SAVEPOINT_TEST_ARGS) { @($env:SAVEPOINT_TEST_ARGS -split '\s+' | Where-Object { $_ }) } else { @() }

# Says which of the arguments the gate does not take. Only a report is allowed; any other argument, a filter in whatever
# spelling, a response file (@file) or an option that lets failures pass (a retry of failed tests among them), could make "gate
# passed" mean less than a full run. A word without dashes is allowed only as the value of an allowed option before it. Matching
# is case sensitive, so an odd spelling is refused, never let through.
function Get-NarrowingArguments {
    param([string[]]$Arguments)
    $afterAllowed = $false
    foreach ($argument in $Arguments) {
        if ($argument -cmatch '^--report-[A-Za-z0-9-]+(=.*)?$') {
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

# What is left of the container's memory limit in MB (the limit less what the container already uses), or $null when there is no
# limit (cgroup v2 says "max", v1 a number near 2^63). SAVEPOINT_GATE_CGROUP_DIR replaces /sys/fs/cgroup.
function Get-CgroupLimitMb {
    $dir = if ($env:SAVEPOINT_GATE_CGROUP_DIR) { $env:SAVEPOINT_GATE_CGROUP_DIR } else { '/sys/fs/cgroup' }
    $pairs = @(
        @((Join-Path $dir 'memory.max'), (Join-Path $dir 'memory.current')),
        @((Join-Path $dir 'memory/memory.limit_in_bytes'), (Join-Path $dir 'memory/memory.usage_in_bytes')))
    foreach ($pair in $pairs) {
        if (-not (Test-Path -LiteralPath $pair[0] -PathType Leaf)) { continue }
        $text = (Get-Content -LiteralPath $pair[0] -Raw).Trim()
        if ($text -notmatch '^[0-9]{1,15}$') { continue }
        $bytes = [long]$text
        if (Test-Path -LiteralPath $pair[1] -PathType Leaf) {
            $used = (Get-Content -LiteralPath $pair[1] -Raw).Trim()
            if ($used -match '^[0-9]{1,15}$') { $bytes = [Math]::Max(0L, $bytes - [long]$used) }
        }
        return [long]($bytes / 1MB)
    }
    return $null
}

# The memory, in MB, that programs can use without the machine swapping or killing something; $null when it cannot be read.
function Get-AvailableMemoryMb {
    if ($env:SAVEPOINT_GATE_AVAILABLE_MEMORY_MB) { return [long]$env:SAVEPOINT_GATE_AVAILABLE_MEMORY_MB }
    try {
        if ($IsWindows -and -not $env:SAVEPOINT_GATE_MEMINFO_FILE) {
            # The performance counter counts the cache that is given back on demand; the OS's "free" figure does not.
            $available = (Get-CimInstance -ClassName Win32_PerfFormattedData_PerfOS_Memory -ErrorAction Stop).AvailableMBytes
            if ($available) { return [long]$available }
            return [long]((Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop).FreePhysicalMemory / 1024)
        }
        $meminfo = if ($env:SAVEPOINT_GATE_MEMINFO_FILE) { $env:SAVEPOINT_GATE_MEMINFO_FILE } else { '/proc/meminfo' }
        if (Test-Path -LiteralPath $meminfo) {
            $line = Get-Content -LiteralPath $meminfo | Where-Object { $_ -match '^MemAvailable:' } | Select-Object -First 1
            if ($line -match '(\d+)') {
                $available = [long]([long]$Matches[1] / 1024)
                # Inside a container /proc/meminfo shows the host's memory, so the container's own limit caps it.
                $limit = Get-CgroupLimitMb
                if ($null -ne $limit -and $limit -lt $available) { return $limit }
                return $available
            }
        }
    }
    catch {
        return $null
    }
    return $null
}

# Says how the App tests run: 'lanes' or 'one', and why. A setting it does not know refuses the run.
function Get-AppMode {
    $setting = $env:SAVEPOINT_GATE_APP_LANES
    if ($setting) {
        if ($setting -eq '1') { return [pscustomobject]@{ Mode = 'one'; Reason = 'SAVEPOINT_GATE_APP_LANES=1' } }
        if ($setting -match '^[1-9][0-9]*$') { return [pscustomobject]@{ Mode = 'lanes'; Reason = "SAVEPOINT_GATE_APP_LANES=$setting" } }
        Write-Host "gate refused: SAVEPOINT_GATE_APP_LANES is '$setting'; use 1 for one App test process or 3 for the lanes." -ForegroundColor Red
        exit 1
    }
    $memory = Get-AvailableMemoryMb
    # Unknown memory takes the layout that needs less of it.
    if ($null -eq $memory) { return [pscustomobject]@{ Mode = 'one'; Reason = 'free memory could not be read; SAVEPOINT_GATE_APP_LANES=3 runs the lanes anyway' } }
    if ($memory -lt $lanesMemoryMb) {
        return [pscustomobject]@{ Mode = 'one'; Reason = "$memory MB free is below $lanesMemoryMb MB; SAVEPOINT_GATE_APP_LANES=3 runs the lanes anyway" }
    }
    return [pscustomobject]@{ Mode = 'lanes'; Reason = "$memory MB free" }
}

# Said first, so a leftover value in the shell is seen before anything runs.
$narrowing = @()
if ($testArgs.Count -gt 0) {
    Write-Host "==> extra test arguments (SAVEPOINT_TEST_ARGS): $($testArgs -join ' ')"
    $narrowing = @(Get-NarrowingArguments $testArgs)
    if ($narrowing.Count -gt 0 -and -not $AllowNarrowedTests) {
        Write-Host "gate refused: SAVEPOINT_TEST_ARGS holds $($narrowing -join ' '), which could run only some of the tests or let a failure pass. Only a report (--report-*) is allowed there; test retries are not available. Unset it, or pass -AllowNarrowedTests if that is on purpose." -ForegroundColor Red
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

# Read once the lock is held: while this gate waited, the gate ahead of it was using the memory.
$appMode = Get-AppMode

function Invoke-Step {
    param([string]$Name, [scriptblock]$Command)
    Write-Host "==> $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

# The App test processes: one per line of build/app-test-lanes.txt, then "rest", which runs everything the lines do not name.
function Get-AppTestLanes {
    $lanes = @()
    foreach ($line in Get-Content -LiteralPath 'build/app-test-lanes.txt') {
        $text = $line.Trim()
        if (-not $text -or $text.StartsWith('#')) { continue }
        $words = @($text -split '\s+')
        if ($words.Count -ne 2 -or $words[0] -eq 'rest') {
            throw "build/app-test-lanes.txt: expected '<name> <namespace>' (the name 'rest' is taken), got '$text'"
        }
        $lanes += [pscustomobject]@{ Name = $words[0]; Namespace = $words[1]; Filter = @('--filter-namespace', $words[1]) }
    }
    $lanes += [pscustomobject]@{ Name = 'rest'; Namespace = $null; Filter = @($lanes | ForEach-Object { '--filter-not-namespace'; $_.Namespace }) }
    return $lanes
}

# Starts every run at once and waits for all of them. Each line a run prints appears at once with the run's name in front, so a
# process that hangs shows where it stopped, and a report of the runs still going comes every SAVEPOINT_GATE_HEARTBEAT_SECONDS
# (300). A failure names the process. A run is { Name; Project; Filter; Threads }.
function Invoke-TestProcesses {
    param([object[]]$Runs)
    $repoRoot = (Get-Location).Path
    $interval = if ($env:SAVEPOINT_GATE_HEARTBEAT_SECONDS) { [double]$env:SAVEPOINT_GATE_HEARTBEAT_SECONDS } else { 300.0 }
    $running = [System.Collections.Concurrent.ConcurrentDictionary[string, bool]]::new()
    foreach ($run in $Runs) { $null = $running.TryAdd($run.Name, $true) }
    $items = @($Runs) + [pscustomobject]@{ Name = '__heartbeat' }
    $outcomes = $items | ForEach-Object -ThrottleLimit $items.Count -Parallel {
        $run = $_
        $running = $using:running
        if ($run.Name -eq '__heartbeat') {
            $watch = [System.Diagnostics.Stopwatch]::StartNew()
            $next = $using:interval
            while ($running.Count -gt 0) {
                Start-Sleep -Milliseconds 250
                if ($watch.Elapsed.TotalSeconds -ge $next) {
                    Write-Host ("==> still running after {0:m\:ss}: {1}" -f $watch.Elapsed, (($running.Keys | Sort-Object) -join ', '))
                    $next += $using:interval
                }
            }
            return
        }

        Set-Location $using:repoRoot
        $filter = @($run.Filter)
        $extra = @($using:testArgs)
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        & dotnet test --project $run.Project -c Release --no-build --max-threads $run.Threads --report-xunit --report-xunit-filename "$($run.Name).xunit" --results-directory $using:resultsDir @filter @extra 2>&1 | ForEach-Object { Write-Host "[$($run.Name)] $_" }
        $code = $LASTEXITCODE
        $null = $running.TryRemove($run.Name, [ref]$null)
        Write-Host ("==> test process '{0}' ended with exit code {1} after {2:m\:ss}" -f $run.Name, $code, $watch.Elapsed)
        [pscustomobject]@{ Name = $run.Name; ExitCode = $code }
    }
    $failed = @($outcomes | Where-Object { $_ -and $_.ExitCode -ne 0 })
    if ($failed.Count -gt 0) {
        throw "test failed in: $(($failed | ForEach-Object { "$($_.Name) (exit code $($_.ExitCode))" }) -join ', ')"
    }
}

try {
    Invoke-Step 'build' { dotnet build Savepoint.slnx -c Release -warnaserror "-m:$buildNodes" -nodeReuse:false }
    # Cleared first so a report left by an earlier run cannot stand in for this one.
    if (Test-Path -LiteralPath $resultsDir) { Remove-Item -LiteralPath $resultsDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
    if ($narrowing.Count -gt 0) {
        # A filter of the caller's cannot be combined with the lanes' filters, so a narrowed run keeps the modules whole.
        Invoke-Step 'test' { dotnet test Savepoint.slnx -c Release --no-build --max-parallel-test-modules 2 --max-threads $testThreads --report-xunit --results-directory $resultsDir @testArgs }
    }
    else {
        $appProject = 'tests/Savepoint.App.Tests'
        $listing = (& dotnet test --project $appProject -c Release --no-build --list-tests 2>&1 | Out-String)
        $listExitCode = $LASTEXITCODE
        # A listed test is "  <class>.<method>(<data>)". A theory whose data cannot be listed shows once in the listing and once per
        # row in a report, so the check counts methods, not cases.
        $appTestCount = @($listing -split '\r?\n' | Where-Object { $_ -match '^  [A-Za-z_][A-Za-z0-9_.+]*\.[A-Za-z_][A-Za-z0-9_]*(\(|$)' } | ForEach-Object { ($_ -replace '\(.*$', '').Trim() } | Sort-Object -Unique -CaseSensitive).Count
        if ($listExitCode -ne 0 -or $appTestCount -eq 0) {
            throw "listing the App tests failed (exit code $listExitCode):`n$listing"
        }
        $runs = @([pscustomobject]@{ Name = 'core'; Project = 'tests/Savepoint.Core.Tests'; Filter = @(); Threads = $testThreads })
        if ($appMode.Mode -eq 'one') {
            Write-Host "==> test (Core, and $appTestCount App test methods): App tests in one process ($($appMode.Reason))"
            $runs += [pscustomobject]@{ Name = 'app'; Project = $appProject; Filter = @(); Threads = $appSingleThreads }
            $appReports = @(Join-Path $resultsDir 'app.xunit')
        }
        else {
            Write-Host "==> test (Core, and $appTestCount App test methods): App tests in lanes ($($appMode.Reason))"
            $lanes = @(Get-AppTestLanes)
            $runs += $lanes | ForEach-Object { [pscustomobject]@{ Name = "app-$($_.Name)"; Project = $appProject; Filter = $_.Filter; Threads = $appThreads } }
            $appReports = @($lanes | ForEach-Object { Join-Path $resultsDir "app-$($_.Name).xunit" })
        }
        Invoke-TestProcesses $runs
        Invoke-Step 'test count' { & ./build/check-test-counts.ps1 -Expected $appTestCount @appReports }
    }
    # A skip probe that breaks turns real tests into skips and the run still passes; this fails on any skip the list does not name.
    Invoke-Step 'skips' { dotnet run --project tools/SkipCheck -c Release --no-build -- --reports $resultsDir --expected build/expected-skips/windows.txt }
    if ($SkipFormat) {
        Write-Host '==> format: skipped (-SkipFormat)'
    }
    else {
        Invoke-Step 'format' { dotnet format Savepoint.slnx --verify-no-changes }
    }
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
