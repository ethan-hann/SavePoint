#!/usr/bin/env bash
# Local gate: build, test, format check. Exits non-zero on the first failure.
#
#   build/gate.sh [--allow-narrowed-tests]
#
# SAVEPOINT_TEST_ARGS takes only a report (--report-*) and retries of failed tests (--retry-failed-tests*). Anything else, such
# as a filter, is refused unless --allow-narrowed-tests says it is on purpose, because a run that tests part of the code or lets a
# failure pass would read as a passed gate.
set -euo pipefail

allow_narrowed=0
for argument in "$@"; do
  case "$argument" in
    --allow-narrowed-tests) allow_narrowed=1 ;;
    *) echo "gate: unknown argument $argument" >&2; exit 2 ;;
  esac
done

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

cd "$(dirname "${BASH_SOURCE[0]}")/.."

# Keep the machine responsive: lower priority, and caps one gate cannot exceed (override per run).
renice -n 10 $$ >/dev/null 2>&1 || true
build_nodes="${SAVEPOINT_GATE_BUILD_NODES:-4}"
test_threads="${SAVEPOINT_GATE_TEST_THREADS:-4}"

# SAVEPOINT_TEST_ARGS is for a report (--report-*) and for retrying failed tests (--retry-failed-tests*). Any other argument, a
# filter in whatever spelling, a response file (@file) or an option that lets failures pass, could make "gate passed" mean less
# than a full run, so it counts as narrowing. A word without dashes is allowed only as the value of an allowed option before it.
# Prints each argument that is not allowed, one per line.
narrowing_arguments() {
  local argument after_allowed=0
  for argument in "$@"; do
    case "$argument" in
      --report-* | --retry-failed-tests | --retry-failed-tests=* | --retry-failed-tests-*)
        case "$argument" in
          *=*) after_allowed=0 ;;
          *) after_allowed=1 ;;
        esac
        ;;
      -* | @*)
        after_allowed=0
        printf '%s\n' "$argument"
        ;;
      *)
        if [ "$after_allowed" -eq 1 ]; then
          after_allowed=0
        else
          printf '%s\n' "$argument"
        fi
        ;;
    esac
  done
}

# Said first, so a leftover value in the shell is seen before anything runs.
narrowing=""
if [ -n "${SAVEPOINT_TEST_ARGS:-}" ]; then
  echo "==> extra test arguments (SAVEPOINT_TEST_ARGS): ${SAVEPOINT_TEST_ARGS}"
  read -r -a test_args <<<"${SAVEPOINT_TEST_ARGS}"
  while read -r argument; do
    narrowing="$narrowing $argument"
  done < <(narrowing_arguments "${test_args[@]}")
  if [ -n "$narrowing" ] && [ "$allow_narrowed" -eq 0 ]; then
    echo "gate refused: SAVEPOINT_TEST_ARGS holds${narrowing}, which could run only some of the tests or let a failure pass. Only a report and retries are allowed there. Unset it, or pass --allow-narrowed-tests if that is on purpose." >&2
    exit 1
  fi
  if [ -n "$narrowing" ]; then
    echo "==> NARROWED on purpose: the extra arguments can leave tests out or let a failure pass, so a pass is not a full gate."
  fi
fi

echo "==> build"
dotnet build Savepoint.slnx -c Release -warnaserror "-m:${build_nodes}" -nodeReuse:false

echo "==> test"
# SAVEPOINT_TEST_ARGS is empty unless a wrapper sets it (build/gate-linux.sh does, for retries); no retry logic lives here.
# shellcheck disable=SC2086
# The gate always asks for the xunit report itself (the skip check reads it), so SAVEPOINT_TEST_ARGS must not.
results_dir="$PWD/artifacts/test-results"
rm -rf "$results_dir"
dotnet test Savepoint.slnx -c Release --no-build --max-parallel-test-modules 2 --max-threads "${test_threads}" --report-xunit --results-directory "$results_dir" ${SAVEPOINT_TEST_ARGS:-}

# A skip probe that breaks turns real tests into skips and the run still passes; this fails on any skip the list does not name.
# SAVEPOINT_EXPECTED_SKIPS names the list in build/expected-skips (build/gate-linux.sh sets linux-wsl); no macOS list exists yet.
expected_skips="${SAVEPOINT_EXPECTED_SKIPS:-}"
if [ -z "$expected_skips" ]; then
  case "$(uname -s)" in
    Linux) expected_skips="linux-ci" ;;
  esac
fi
if [ -n "$expected_skips" ]; then
  echo "==> skips (expected: build/expected-skips/${expected_skips}.txt)"
  dotnet run --project tools/SkipCheck -c Release --no-build -- --reports "$results_dir" --expected "build/expected-skips/${expected_skips}.txt"
else
  echo "==> skips: no list for $(uname -s), not checked"
fi

echo "==> format"
dotnet format Savepoint.slnx --verify-no-changes

if [ -n "$narrowing" ]; then
  echo "gate passed, NARROWED: not a full gate"
else
  echo "gate passed"
fi
