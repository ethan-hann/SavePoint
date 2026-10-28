#!/usr/bin/env bash
# Runs build/gate.sh on the Linux test VM. Started over SSH by build/gate-linux.ps1, which uploads the commit as a tar.gz beside
# this script and a run.env file in one folder; that folder is the only argument.
#
# The VM is a real Ubuntu on ext4 and the gate runs as an ordinary user, the way the CI Linux job does, so the CI skip list
# (build/expected-skips/linux-ci.txt) applies. One gate at a time, and none while a package build runs on the VM: such a run is
# refused at once. When the run ends, the SDKs and runtimes an install left behind and the NuGet packages unread for 30 days are
# removed from the gate's folder.
#
# Exit codes: the gate's own, or 75 when a gate or a package build already holds the VM, or 1 for a setup failure.
set -euo pipefail

# Absolute, because the script changes directory before its trap removes the folder.
incoming="$(cd "${1:?usage: gate-linux.sh <upload folder>}" && pwd)"
trap 'rm -rf "$incoming"' EXIT

fail() {
  echo "gate-linux: $*" >&2
  exit 1
}

# run.env holds GATE_ID, GATE_COMMIT, GATE_DIR, GATE_ALLOW_NARROWED and the optional SAVEPOINT_* settings, each single-quoted.
# shellcheck disable=SC1091
. "$incoming/run.env"
# shellcheck disable=SC1091
. "$incoming/vm-common.sh"

case "$GATE_DIR" in
  /*) root="$GATE_DIR" ;;
  *) root="$HOME/$GATE_DIR" ;;
esac
mkdir -p "$root/logs" "$root/incoming"

# The lock is taken before the tree is touched, so a refused run changes nothing. Its holder is written beside it because opening
# the lock file for writing would erase it.
exec 9>>"$root/gate.lock"
if ! flock -n 9; then
  echo "gate-linux: another gate is running on this VM; this run is refused and changed nothing." >&2
  [ -f "$root/gate.holder" ] && sed 's/^/gate-linux:   /' "$root/gate.holder" >&2
  exit 75
fi
# A package build on the VM holds this too, and the two must not run together.
vm_take_heavy_lock gate-linux "$GATE_COMMIT"
printf 'pid %s, commit %s, started %s\n(stop it with: kill -TERM %s)\n' "$$" "$GATE_COMMIT" "$(date '+%F %T')" "$$" >"$root/gate.holder"
# The driver stops an interrupted run through this file; TERM stops the whole session (build, test host, children), not just this shell.
printf '%s\n' "$$" >"$root/run-$GATE_ID.pid"
trap 'rm -f "$root/gate.holder" "$root/run-$GATE_ID.pid"; vm_release_heavy_lock; rm -rf "$incoming"' EXIT
trap 'trap - TERM INT HUP; kill -TERM 0' TERM INT HUP

# Old uploads from runs that died, and all but the newest logs.
find "$root/incoming" -mindepth 1 -maxdepth 1 -mmin +1440 -exec rm -rf {} + 2>/dev/null || true
find "$root/logs" -maxdepth 1 -type f -name '*.log' -printf '%T@ %p\n' | sort -rn | tail -n +21 | cut -d' ' -f2- | xargs -r rm -f

for tool in rsync gpg flock; do
  command -v "$tool" >/dev/null 2>&1 || fail "$tool is missing on this VM; install it with: sudo apt install $tool"
done
[ -x /usr/bin/dbus-daemon ] || [ -x /bin/dbus-daemon ] || fail "dbus-daemon is missing on this VM (the notification tests need it); install it with: sudo apt install dbus"

# The commit goes into a fixed tree, so build outputs survive between runs. rsync compares content and gives a changed file the
# current time, so an old commit's file times never make the build think an output is newer than its source. Folders the build
# makes are left alone.
tree="$root/tree"
mkdir -p "$tree" "$incoming/src"
tar -xzf "$incoming/commit.tar.gz" -C "$incoming/src"
rsync -rlp --checksum --delete \
  --exclude='/artifacts/' --exclude='/.tools/' --exclude='/resources/git/' --exclude='/render-output/' --exclude='TestResults/' \
  --exclude='bin/' --exclude='obj/' \
  "$incoming/src/" "$tree/"

export DOTNET_CLI_HOME="$root/home"
export NUGET_PACKAGES="$root/nuget"
export NUGET_HTTP_CACHE_PATH="$root/nuget-http"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export POWERSHELL_TELEMETRY_OPTOUT=1
# Build servers and reused nodes would outlive the run.
export DOTNET_CLI_USE_MSBUILD_SERVER=0
export MSBUILDDISABLENODEREUSE=1
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES" "$NUGET_HTTP_CACHE_PATH"

# global.json rolls forward to the newest feature band, so any SDK of its major.minor satisfies it. The CI job installs the same way.
dotnet_root="$root/dotnet"
sdk_line="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([0-9]*\.[0-9]*\)\.[0-9]*.*/\1/p' "$tree/global.json" | head -n 1)"
[ -n "$sdk_line" ] || fail "no sdk version in $tree/global.json"
if ! [ -x "$dotnet_root/dotnet" ] || ! "$dotnet_root/dotnet" --list-sdks | grep -q "^$sdk_line\."; then
  echo "==> installing the .NET $sdk_line SDK into $dotnet_root"
  bash "$tree/build/install-dotnet.sh" "$dotnet_root" "$sdk_line"
fi
export DOTNET_ROOT="$dotnet_root"
export PATH="$dotnet_root:$PATH"

# Tests drive the PowerShell scripts of build/, which skip without pwsh. The CI job installs it as a .NET tool at the version its
# workflow names, and so does this.
pwsh_version="$(sed -n 's/.*tool install .*PowerShell --version \([0-9][0-9.]*\).*/\1/p' "$tree/.forgejo/workflows/gate.yml" | head -n 1)"
[ -n "$pwsh_version" ] || fail "no PowerShell version in $tree/.forgejo/workflows/gate.yml"
pwsh_root="$root/pwsh"
if ! [ -x "$pwsh_root/pwsh" ] || [ "$("$pwsh_root/pwsh" -NoProfile -Command '$PSVersionTable.PSVersion.ToString()')" != "$pwsh_version" ]; then
  echo "==> installing PowerShell $pwsh_version into $pwsh_root"
  rm -rf "$pwsh_root"
  dotnet tool install --tool-path "$pwsh_root" PowerShell --version "$pwsh_version"
fi
export PATH="$pwsh_root:$PATH"

# Settings the driver passes on; unset ones keep gate.sh's defaults.
for name in SAVEPOINT_TEST_ARGS SAVEPOINT_GATE_BUILD_NODES SAVEPOINT_GATE_TEST_THREADS SAVEPOINT_GATE_APP_LANES; do
  if [ -n "${!name:-}" ]; then export "$name"; else unset "$name"; fi
done
gate_args=()
[ "${GATE_ALLOW_NARROWED:-0}" = 1 ] && gate_args+=(--allow-narrowed-tests)

log="$root/logs/$GATE_ID.log"
echo "==> gating $GATE_COMMIT on $(hostname) as $(id -un), .NET SDK $(dotnet --version), PowerShell $pwsh_version"
echo "==> tree $tree (log $log)"
cd "$tree"
# Standard input is closed and the lock is not inherited, so nothing the gate starts can read the terminal or outlive the lock.
# The gate runs in the background and is waited for, because bash holds a trapped signal until a foreground command ends, and
# an interrupted run has to stop at once.
status_file="$incoming/status"
{
  set +e
  bash build/gate.sh "${gate_args[@]}" </dev/null 8>&- 9>&- 2>&1
  echo "$?" >"$status_file"
} | {
  while IFS= read -r line || [ -n "$line" ]; do
    case "$line" in
      '==> '*) printf '[%4ss] %s\n' "$SECONDS" "$line" ;;
      *) printf '%s\n' "$line" ;;
    esac
  done
} | tee "$log" &
wait "$!" || true
status="$(cat "$status_file" 2>/dev/null || echo 1)"

# The compiler server the build starts would otherwise stay on the VM for ten idle minutes.
dotnet build-server shutdown >/dev/null 2>&1 || true

# Last, so the pruning never meets a build: the SDKs the install left behind, and packages unread for 30 days.
vm_prune_caches "$dotnet_root" "$NUGET_PACKAGES" "$NUGET_HTTP_CACHE_PATH"

echo "gate-linux: gate exited with $status after ${SECONDS}s (log $log)"
exit "$status"
