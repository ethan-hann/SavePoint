#!/usr/bin/env bash
# Local gate: build, test, format check. Exits non-zero on the first failure.
set -euo pipefail

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

cd "$(dirname "${BASH_SOURCE[0]}")/.."

# Keep the machine responsive: lower priority, and caps one gate cannot exceed (override per run).
renice -n 10 $$ >/dev/null 2>&1 || true
build_nodes="${SAVEPOINT_GATE_BUILD_NODES:-4}"
test_threads="${SAVEPOINT_GATE_TEST_THREADS:-8}"

echo "==> build"
dotnet build Savepoint.slnx -c Release -warnaserror "-m:${build_nodes}" -nodeReuse:false

echo "==> test"
# SAVEPOINT_TEST_ARGS is empty unless a wrapper sets it (build/gate-linux.sh does, for retries); no retry logic lives here.
# shellcheck disable=SC2086
dotnet test Savepoint.slnx -c Release --no-build --max-parallel-test-modules 1 --max-threads "${test_threads}" ${SAVEPOINT_TEST_ARGS:-}

echo "==> format"
dotnet format Savepoint.slnx --verify-no-changes

echo "gate passed"
