#!/usr/bin/env bash
# Local gate: build, test, format check. Exits non-zero on the first failure.
set -euo pipefail

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

cd "$(dirname "${BASH_SOURCE[0]}")/.."

echo "==> build"
dotnet build Savepoint.slnx -c Release -warnaserror

echo "==> test"
# SAVEPOINT_TEST_ARGS is empty unless a wrapper sets it (build/gate-linux.sh does, for retries); no retry logic lives here.
# shellcheck disable=SC2086
dotnet test Savepoint.slnx -c Release --no-build ${SAVEPOINT_TEST_ARGS:-}

echo "==> format"
dotnet format Savepoint.slnx --verify-no-changes

echo "gate passed"
