#!/usr/bin/env bash
# Fails when the xunit reports of the split App test processes hold a different number of test methods than the full listing.
#
#   build/check-test-counts.sh <expected> <report>...
#
# The gates run the App tests in several processes, each with a filter (build/app-test-lanes.txt). A filter that leaves a test out, or a
# process that ends early, would let the gate pass with tests missing, so the processes' test methods must add up to the test methods
# of `dotnet test --list-tests`. Methods are counted, not cases: a theory whose data cannot be listed shows as one entry in the listing
# and as one case per row in a report. Exit code 0 when they match, 1 when they differ or a report is missing or unreadable.
set -euo pipefail

if [ "$#" -lt 2 ] || ! [[ "$1" =~ ^[0-9]+$ ]]; then
  echo "usage: check-test-counts.sh <expected> <report>..." >&2
  exit 2
fi

expected="$1"
shift

sum=0
parts=""
for report in "$@"; do
  if [ ! -f "$report" ]; then
    echo "test count check failed: $report does not exist, so its test process wrote no report." >&2
    exit 1
  fi
  # A test element ends in type="<class>" method="<name>"; a method counts once however many rows its cases have.
  count="$({ grep -o 'type="[^"]*" method="[^"]*"' "$report" || true; } | LC_ALL=C sort -u | wc -l | tr -d ' ')"
  sum=$((sum + count))
  parts="${parts:+$parts + }$count"
done

if [ "$sum" -ne "$expected" ]; then
  echo "test count check failed: the test processes ran $parts = $sum test methods, but the full listing has $expected. A test is in no process or in two, or a process ended early. Check build/app-test-lanes.txt." >&2
  exit 1
fi

echo "test count check: $parts = $sum test methods, as many as the full listing."
