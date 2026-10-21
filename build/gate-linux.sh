#!/usr/bin/env bash
# Linux gate: build/gate.sh inside the .linux-gate worktree, with a .NET SDK and caches kept in .tools/.
# Started by build/gate-linux.ps1, which creates the worktree and checks the commit out (Linux git cannot follow its Windows paths).
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tools="$repo/.tools"
gate_tree="$repo/.linux-gate"
dotnet_root="$tools/dotnet-linux"

fail() {
  echo "gate-linux: $*" >&2
  exit 1
}

[ -f "$gate_tree/build/gate.sh" ] || fail "$gate_tree is not a checked-out worktree; run build/gate-linux.ps1 from Windows, which creates it"

export HOME="$tools/home-linux"
export DOTNET_CLI_HOME="$HOME"
export NUGET_PACKAGES="$tools/nuget-linux"
export NUGET_HTTP_CACHE_PATH="$tools/nuget-http-linux"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
# Build servers and reused nodes would outlive the run, and shutting them down could hit other sessions.
export DOTNET_CLI_USE_MSBUILD_SERVER=0
export MSBUILDDISABLENODEREUSE=1
mkdir -p "$HOME" "$NUGET_PACKAGES" "$NUGET_HTTP_CACHE_PATH"

# global.json rolls forward to the newest feature band, so the newest SDK of its major.minor satisfies it.
sdk_version="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([0-9]*\.[0-9]*\)\.[0-9]*.*/\1/p' "$gate_tree/global.json" | head -n 1)"
[ -n "$sdk_version" ] || fail "no sdk version in $gate_tree/global.json"

if ! [ -x "$dotnet_root/dotnet" ] || ! "$dotnet_root/dotnet" --list-sdks | grep -q "^$sdk_version\."; then
  echo "==> installing the .NET $sdk_version SDK into $dotnet_root"
  mkdir -p "$tools"
  installer="$tools/dotnet-install.sh"
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL --retry 3 --retry-delay 2 -o "$installer" https://dot.net/v1/dotnet-install.sh
  else
    wget -q --tries=3 -O "$installer" https://dot.net/v1/dotnet-install.sh
  fi
  bash "$installer" --channel "$sdk_version" --install-dir "$dotnet_root" --no-path
fi
export DOTNET_ROOT="$dotnet_root"
export PATH="$dotnet_root:$PATH"

# The tests keep their temp files in <tree>/.tools/tmp/linux; this says what that folder can do.
temp_folder="$gate_tree/.tools/tmp/linux"
probe="$temp_folder/case-probe-$$"
mkdir -p "$probe"
mkdir "$probe/a"
if mkdir "$probe/A" 2>/dev/null; then
  echo "==> temp folder $temp_folder is case sensitive"
else
  echo "==> WARNING: temp folder $temp_folder is NOT case sensitive (a and A are one directory); tests of case-sensitive behavior can fail here for that reason alone" >&2
fi
rm -rf "$probe"

echo "==> .NET SDK $(dotnet --version) from $dotnet_root, gating $gate_tree"
cd "$gate_tree"
exec bash build/gate.sh
