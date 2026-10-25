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

# Tests drive the PowerShell scripts of build/, which skip without pwsh; the .NET SDK installs it as a tool, as the CI job does.
pwsh_root="$tools/pwsh-linux"
if ! [ -x "$pwsh_root/pwsh" ]; then
  echo "==> installing PowerShell into $pwsh_root"
  "$dotnet_root/dotnet" tool install --tool-path "$pwsh_root" PowerShell --version 7.6.8
fi
export PATH="$pwsh_root:$PATH"

# The tests keep their temp files in <tree>/.tools/tmp/linux; this says what that folder can do.
temp_folder="$gate_tree/.tools/tmp/linux"
probe="$temp_folder/case-probe-$$"
# The warnings below print now, go into the log first, and print again after the gate, so a green run does not hide them.
warnings=""
mkdir -p "$probe"
mkdir "$probe/a"
if mkdir "$probe/A" 2>/dev/null; then
  echo "==> temp folder $temp_folder is case sensitive"
else
  case_warning="==> WARNING: temp folder $temp_folder is NOT case sensitive (a and A are one directory); tests of case-sensitive behavior can fail here for that reason alone"
  echo "$case_warning" >&2
  warnings="$warnings$case_warning"$'\n'
fi
rm -rf "$probe"

# Tests that make a file or folder unreadable skip where permissions do not hold: for a privileged user, and on a file system that
# keeps none (a Windows drive mounted under WSL). Each skip carries its reason, but a green gate would not say that a whole group of
# tests never ran, so this says it first and names the files the tests live in.
probe="$temp_folder/permission-probe-$$"
mkdir -p "$probe"
printf x >"$probe/file"
chmod 000 "$probe/file"
permissions_reason=""
if [ "$(id -u)" -eq 0 ]; then
  permissions_reason="the gate runs as a privileged user"
elif cat "$probe/file" >/dev/null 2>&1; then
  permissions_reason="chmod has no effect in $temp_folder"
fi
chmod 600 "$probe/file"
rm -rf "$probe"
if [ -n "$permissions_reason" ]; then
  skipping="$(grep -rlE 'HeldFiles\.(Hold|MakeUnreadable|SkipUnlessPermissionsHold|PreventDeleting)|IsPrivilegedProcess' "$gate_tree/tests" --include='*.cs' 2>/dev/null | grep -v '/obj/' | grep -v '/HeldFiles\.cs$' | sed 's#.*/##; s#\.cs$##' | sort -u | tr '\n' ' ' || true)"
  permissions_warning="==> WARNING: file permissions do not hold here ($permissions_reason), so the tests that make a file or folder unreadable SKIP, in: ${skipping:-none found}"
  echo "$permissions_warning" >&2
  warnings="$warnings$permissions_warning"$'\n'
else
  echo "==> file permissions hold in $temp_folder"
fi

# D27: on this 9p mount a file just renamed into place can be briefly invisible, so file-heavy tests fail at random.
# Failed tests are retried up to this many times and every one that needed a retry is listed below; a test that
# fails every time still fails the gate. Only this script asks for retries: it builds the retry extension in
# (build/TestRetry.targets) and passes the option through build/gate.sh. The Windows gate never retries.
retries=2
export SAVEPOINT_TEST_RETRIES="$retries"
export SAVEPOINT_TEST_ARGS="--retry-failed-tests $retries"
# This workspace skips more than CI does (no sockets, no permissions on the 9p mount), so it has its own skip list.
export SAVEPOINT_EXPECTED_SKIPS=linux-wsl

echo "==> .NET SDK $(dotnet --version) from $dotnet_root, gating $gate_tree (failed tests are retried up to $retries times)"
cd "$gate_tree"
mkdir -p "$tools"
log="$tools/gate-linux-last.log"
printf '%s' "$warnings" >"$log"
set +e
bash build/gate.sh 2>&1 | tee -a "$log"
status="${PIPESTATUS[0]}"
set -e

bash build/report-retried-tests.sh "$log" "$retries"

# The last lines of the run and of the log say what the gate could not check, however the gate went.
if [ -n "$warnings" ]; then
  printf '%s' "$warnings" | tee -a "$log" >&2
fi

exit "$status"
