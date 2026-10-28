#!/usr/bin/env bash
# Runs build/package.sh on the Linux test VM. Started over SSH by build/package-linux.ps1, which uploads the commit as a tar.gz
# beside this script and a run.env file in one folder; that folder is the only argument.
#
# The VM is a real Ubuntu on ext4. The package is built as an ordinary user in a folder of its own (default ~/package), apart from
# the gate's tree, so a package run and a gate run never share bin/, obj/ or an SDK. One package run at a time, and none while a gate
# runs on the VM: such a run is refused at once. When the build succeeds, the package files are copied to <folder>/out/<run id>/release
# for the driver to fetch. When the run ends, the SDKs and runtimes an install left behind and the NuGet packages unread for 30 days
# are removed from the package folder.
#
# Exit codes: package.sh's own, or 75 when a package run or a gate already holds the VM, or 1 for a setup failure.
set -euo pipefail

# Absolute, because the script changes directory before its trap removes the folder.
incoming="$(cd "${1:?usage: package-linux.sh <upload folder>}" && pwd)"
trap 'rm -rf "$incoming"' EXIT

fail() {
  echo "package-linux: $*" >&2
  exit 1
}

# run.env holds PACKAGE_ID, PACKAGE_COMMIT, PACKAGE_DIR and PACKAGE_SOURCES, each single-quoted.
# shellcheck disable=SC1091
. "$incoming/run.env"
# shellcheck disable=SC1091
. "$incoming/vm-common.sh"

case "$PACKAGE_DIR" in
  /*) root="$PACKAGE_DIR" ;;
  *) root="$HOME/$PACKAGE_DIR" ;;
esac
mkdir -p "$root/logs" "$root/incoming" "$root/out"

# The lock is taken before the tree is touched, so a refused run changes nothing. Its holder is written beside it because opening
# the lock file for writing would erase it.
exec 9>>"$root/package.lock"
if ! flock -n 9 && ! { vm_free_stale_lock "$root/package.lock" "$root/package.holder" && flock -w 10 9; }; then
  echo "package-linux: another package run is on this VM; this run is refused and changed nothing." >&2
  [ -f "$root/package.holder" ] && sed 's/^/package-linux:   /' "$root/package.holder" >&2
  exit 75
fi
# A gate on the VM holds this too, and the two must not run together.
vm_take_heavy_lock package-linux "$PACKAGE_COMMIT"
printf 'pid %s, commit %s, started %s\n(stop it with: kill -TERM %s)\n' "$$" "$PACKAGE_COMMIT" "$(date '+%F %T')" "$$" >"$root/package.holder"
# The driver stops an interrupted run through this file; TERM stops the whole session (publish, vpk, children), not just this shell.
printf '%s\n' "$$" >"$root/run-$PACKAGE_ID.pid"
trap 'rm -f "$root/package.holder" "$root/run-$PACKAGE_ID.pid"; vm_release_heavy_lock; rm -rf "$incoming"' EXIT
trap 'trap - TERM INT HUP; kill -TERM 0' TERM INT HUP

# Old uploads from runs that died, and all but the newest logs and outputs.
find "$root/incoming" -mindepth 1 -maxdepth 1 -mmin +1440 -exec rm -rf {} + 2>/dev/null || true
find "$root/logs" -maxdepth 1 -type f -name '*.log' -printf '%T@ %p\n' | sort -rn | tail -n +11 | cut -d' ' -f2- | xargs -r rm -f
find "$root/out" -mindepth 1 -maxdepth 1 -type d -printf '%T@ %p\n' | sort -rn | tail -n +4 | cut -d' ' -f2- | xargs -r rm -rf

# mksquashfs is what Velopack builds the AppImage with.
for tool in rsync flock curl mksquashfs; do
  command -v "$tool" >/dev/null 2>&1 || fail "$tool is missing on this VM; install it with: sudo apt install $([ "$tool" = mksquashfs ] && echo squashfs-tools || echo "$tool")"
done

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

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
# Build servers and reused nodes would outlive the run.
export DOTNET_CLI_USE_MSBUILD_SERVER=0
export MSBUILDDISABLENODEREUSE=1

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

package_args=(--revision "${PACKAGE_COMMIT:0:12}" --tools-dir "$root/tools")
[ "${PACKAGE_SOURCES:-0}" = 1 ] && package_args+=(--sources)

log="$root/logs/$PACKAGE_ID.log"
echo "==> packaging $PACKAGE_COMMIT on $(hostname) as $(id -un), .NET SDK $(dotnet --version)"
echo "==> tree $tree (log $log)"
cd "$tree"
# Standard input is closed and the lock is not inherited, so nothing the build starts can read the terminal or outlive the lock.
# The script runs in the background and is waited for, because bash holds a trapped signal until a foreground command ends, and
# an interrupted run has to stop at once.
status_file="$incoming/status"
{
  set +e
  bash build/package.sh "${package_args[@]}" </dev/null 8>&- 9>&- 2>&1
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

if [ "$status" -eq 0 ]; then
  # Copied out under the lock, so the next run cannot overwrite what the driver is about to fetch.
  out="$root/out/$PACKAGE_ID/release"
  rm -rf "$root/out/$PACKAGE_ID"
  mkdir -p "$out"
  cp -a "$tree/artifacts/release/linux-x64" "$out/"
  [ -f "$tree/artifacts/release/contents-linux-x64.txt" ] && cp -a "$tree/artifacts/release/contents-linux-x64.txt" "$out/"
  if [ "${PACKAGE_SOURCES:-0}" = 1 ] && [ -d "$tree/artifacts/release/source" ]; then
    cp -a "$tree/artifacts/release/source" "$out/"
  fi
fi

# Last, so the pruning never meets a build: the SDKs the install left behind, and packages unread for 30 days. These are the
# caches package.sh was given with --tools-dir.
vm_prune_caches "$dotnet_root" "$root/tools/nuget-linux" "$root/tools/nuget-http-linux"

echo "package-linux: package.sh exited with $status after ${SECONDS}s (log $log)"
exit "$status"
