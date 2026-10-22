#!/usr/bin/env bash
# Local gate: build, test, format check. Exits non-zero on the first failure.
#
#   build/gate.sh [--allow-narrowed-tests]
#
# --allow-narrowed-tests lets a test filter in SAVEPOINT_TEST_ARGS through. Without it a filter is refused, because a run that
# tests part of the code would read as a passed gate.
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

# Said first, so a leftover value in the shell is seen before anything runs. An option that picks which tests run (or only lists
# them) makes the gate a partial one.
narrowing=""
if [ -n "${SAVEPOINT_TEST_ARGS:-}" ]; then
  echo "==> extra test arguments (SAVEPOINT_TEST_ARGS): ${SAVEPOINT_TEST_ARGS}"
  for argument in ${SAVEPOINT_TEST_ARGS}; do
    case "$argument" in
      --filter | --filter-* | --treenode-filter | --list-tests) narrowing="$narrowing $argument" ;;
    esac
  done
  if [ -n "$narrowing" ] && [ "$allow_narrowed" -eq 0 ]; then
    echo "gate refused: SAVEPOINT_TEST_ARGS holds${narrowing}, which would run only some of the tests. Unset it, or pass --allow-narrowed-tests if the narrowing is on purpose." >&2
    exit 1
  fi
  if [ -n "$narrowing" ]; then
    echo "==> NARROWED on purpose: only the tests the filter picks run, so a pass is not a full gate."
  fi
fi

echo "==> build"
dotnet build Savepoint.slnx -c Release -warnaserror "-m:${build_nodes}" -nodeReuse:false

echo "==> test"
# SAVEPOINT_TEST_ARGS is empty unless a wrapper sets it (build/gate-linux.sh does, for retries); no retry logic lives here.
# shellcheck disable=SC2086
dotnet test Savepoint.slnx -c Release --no-build --max-parallel-test-modules 1 --max-threads "${test_threads}" ${SAVEPOINT_TEST_ARGS:-}

echo "==> format"
dotnet format Savepoint.slnx --verify-no-changes

if [ -n "$narrowing" ]; then
  echo "gate passed, NARROWED: only the tests the filter picks ran"
else
  echo "gate passed"
fi
