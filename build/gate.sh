#!/usr/bin/env bash
# Local gate: build, test, format check. Exits non-zero on the first failure.
set -euo pipefail

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

cd "$(dirname "${BASH_SOURCE[0]}")/.."

echo "==> build"
dotnet build Savepoint.slnx -c Release -warnaserror

echo "==> test"
dotnet test Savepoint.slnx -c Release --no-build

echo "==> format"
dotnet format Savepoint.slnx --verify-no-changes

echo "gate passed"
