#!/usr/bin/env bash
# Lists the tests that failed on an early attempt of a run made with --retry-failed-tests and passed on a later one.
# Usage: report-retried-tests.sh <test output log> <retries>
# The retry extension prefixes every failure with its attempt: "failed (try 2) Some.Test.Name (12ms)". A test that is
# still failing on the last attempt (retries + 1) is not listed; the run's own output and exit code already say it failed.
set -euo pipefail

log="$1"
retries="$2"

failures="$(sed 's/\x1b\[[0-9;]*m//g' "$log" | sed -n 's/^failed (try \([0-9]*\)) \(.*\) ([0-9a-z .]*)$/\1\t\2/p')"
[ -n "$failures" ] || exit 0

recovered="$(awk -F'\t' -v last="$((retries + 1))" '
  $1 == last { dead[$2] = 1 }
  { seen[$2] = 1 }
  END { for (name in seen) if (!(name in dead)) print name }
' <<<"$failures" | sort)"
[ -n "$recovered" ] || exit 0

echo
echo "==> $(printf '%s\n' "$recovered" | wc -l | tr -d ' ') test(s) failed on an early attempt and passed on a retry:"
while IFS= read -r name; do
  echo "    $name"
done <<<"$recovered"
echo "==> the 9p mount is the likely cause (D27); a test that keeps failing still fails the gate"
