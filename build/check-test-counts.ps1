#Requires -Version 7
<#
.SYNOPSIS
Fails when the xunit reports of the split App test processes hold a different number of test methods than the full listing.

.DESCRIPTION
The gates run the App tests in several processes, each with a filter (build/app-test-lanes.txt). A filter that leaves a test out, or a
process that ends early, would let the gate pass with tests missing, so the processes' test methods must add up to the test methods
of `dotnet test --list-tests`. Methods are counted, not cases: a theory whose data cannot be listed shows as one entry in the listing
and as one case per row in a report. Exit code 0 when they match, 1 when they differ or a report is missing or unreadable.

.PARAMETER Expected
The number of distinct test methods the full listing found.

.PARAMETER Reports
The xunit report of each process, as paths.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$Expected,
    [Parameter(Mandatory, ValueFromRemainingArguments)][string[]]$Reports
)

$ErrorActionPreference = 'Stop'

$counts = @()
foreach ($report in $Reports) {
    if (-not (Test-Path -LiteralPath $report -PathType Leaf)) {
        Write-Host "test count check failed: $report does not exist, so its test process wrote no report." -ForegroundColor Red
        exit 1
    }
    try {
        $document = [xml](Get-Content -LiteralPath $report -Raw)
    }
    catch {
        Write-Host "test count check failed: $report could not be read: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
    $methods = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($test in $document.SelectNodes('//test')) {
        [void]$methods.Add($test.GetAttribute('type') + '.' + $test.GetAttribute('method'))
    }
    $counts += $methods.Count
}

$sum = ($counts | Measure-Object -Sum).Sum
$parts = $counts -join ' + '
if ($sum -ne $Expected) {
    Write-Host "test count check failed: the test processes ran $parts = $sum test methods, but the full listing has $Expected. A test is in no process or in two, or a process ended early. Check build/app-test-lanes.txt." -ForegroundColor Red
    exit 1
}

Write-Host "test count check: $parts = $sum test methods, as many as the full listing."
exit 0
