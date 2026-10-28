#!/usr/bin/env bash
# Shared by build/gate-linux.sh and build/package-linux.sh on the Linux test VM; each driver uploads it beside its own script and the
# script sources it. Not run by hand.

# One lock for heavy work on the whole VM, besides each folder's own lock. The VM has four cores, and a package build running beside
# a gate would starve the tests that depend on timing, with no retries left (D60.3). The lock sits in the account's home, outside
# both folders, and its holder is written beside it because opening the lock file for writing would erase it.
#
# The wait, in seconds, between the steps of clearing a stale lock (a note must stay stale across it, a program gets this long to end).
vm_stale_settle="${SAVEPOINT_VM_STALE_SETTLE_SECONDS:-3}"

# Says whether the run that a holder note names is over: there is no note, it names no process, the process does not exist, or the
# note was written before the machine last booted (so a process number the new boot reused cannot keep it alive).
# vm_holder_is_stale <holder note>
vm_holder_is_stale() {
  local holder="$1" pid boot
  [ -f "$holder" ] || return 0
  pid="$(sed -n 's/.*pid \([0-9][0-9]*\).*/\1/p' "$holder" | head -n 1)"
  [ -n "$pid" ] || return 0
  kill -0 "$pid" 2>/dev/null || return 0
  boot="$(sed -n 's/^btime //p' /proc/stat 2>/dev/null | head -n 1)"
  if [ -n "$boot" ] && [ "$(stat -c %Y "$holder")" -lt "$boot" ]; then
    return 0
  fi
  return 1
}

# For a lock that is held although its note says the run is over: ends the programs that still have the lock file open (what a
# killed run's children leave behind). The kernel drops a lock with its process, so after a reset nothing holds it and this is not
# needed; it is for the orphans of a run that was killed. Returns 0 when it ended some, 1 when the holder is alive or none is found.
# The note must be stale twice, a few seconds apart, so a run that has just taken the lock and not yet written its note is left alone.
# vm_free_stale_lock <lock file> <holder note>
vm_free_stale_lock() {
  local lock="$1" holder="$2" fd pid pids=()
  vm_holder_is_stale "$holder" || return 1
  sleep "$vm_stale_settle"
  vm_holder_is_stale "$holder" || return 1
  for fd in /proc/[0-9]*/fd/*; do
    [ "$(readlink "$fd" 2>/dev/null)" = "$lock" ] || continue
    pid="${fd#/proc/}"
    pid="${pid%%/*}"
    [ "$pid" = "$$" ] || pids+=("$pid")
  done
  [ "${#pids[@]}" -gt 0 ] || return 1
  echo "the note beside $lock names a run that is over, but process ${pids[*]} still holds the lock; ending it." >&2
  kill -TERM "${pids[@]}" 2>/dev/null || true
  sleep "$vm_stale_settle"
  kill -KILL "${pids[@]}" 2>/dev/null || true
  sleep 1
  return 0
}

# vm_take_heavy_lock <what is running> <commit>
# Takes the lock on file descriptor 8 for the rest of the script, or says who holds it and exits with 75, before anything is changed.
# A lock held only by leftovers of a run that is over is cleared first (see vm_free_stale_lock).
vm_take_heavy_lock() {
  local dir="$HOME/.savepoint-vm"
  mkdir -p "$dir"
  exec 8>>"$dir/heavy.lock"
  if ! flock -n 8 && ! { vm_free_stale_lock "$dir/heavy.lock" "$dir/heavy.holder" && flock -w 10 8; }; then
    echo "$1: the VM is busy with another heavy run (a gate or a package build); this run is refused and changed nothing. Run it again when that one has finished." >&2
    [ -f "$dir/heavy.holder" ] && sed 's/^/'"$1"':   /' "$dir/heavy.holder" >&2
    exit 75
  fi
  printf '%s, pid %s, commit %s, started %s\n(stop it with: kill -TERM %s)\n' "$1" "$$" "$2" "$(date '+%F %T')" "$$" >"$dir/heavy.holder"
}

# Lets go of the holder note; the lock itself goes with the process.
vm_release_heavy_lock() {
  rm -f "$HOME/.savepoint-vm/heavy.holder"
}

# Keeps the newest folder of each major.minor line in a folder that holds one folder per version, and removes the others.
vm_prune_versions() {
  local dir="$1" version line previous=""
  [ -d "$dir" ] || return 0
  while IFS= read -r version; do
    case "$version" in [0-9]*.[0-9]*.*) ;; *) continue ;; esac
    line="$(printf '%s' "$version" | cut -d. -f1,2)"
    if [ "$line" = "$previous" ]; then
      rm -rf -- "${dir:?}/$version"
    else
      previous="$line"
    fi
  done < <(ls -1 "$dir" | sort -rV)
}

# Removes the SDKs and runtimes that installing a newer one left behind. dotnet-install adds a version and never takes one away, and
# the scripts only ever run the newest SDK of the major.minor line in global.json.
vm_prune_dotnet() {
  local root="$1" dir
  [ -d "$root" ] || return 0
  vm_prune_versions "$root/sdk"
  vm_prune_versions "$root/host/fxr"
  for dir in "$root"/shared/* "$root"/packs/*; do
    [ -d "$dir" ] && vm_prune_versions "$dir"
  done
}

# Removes the NuGet packages (<id>/<version> folders) none of whose files were read or written in the last 30 days. A package a
# build uses is read at least once a day, so one that goes unread for a month is gone from every project, and the next restore that
# wants it downloads it again. Folders are judged by every file in them, so a file system that does not record reads cannot mislead
# it into dropping a package that changed recently.
vm_prune_nuget_packages() {
  local dir="$1" package
  [ -d "$dir" ] || return 0
  while IFS= read -r package; do
    if [ -z "$(find "$package" -type f \( -atime -30 -o -mtime -30 \) -print -quit)" ]; then
      rm -rf -- "$package"
    fi
  done < <(find "$dir" -mindepth 2 -maxdepth 2 -type d)
  find "$dir" -mindepth 1 -maxdepth 1 -type d -empty -delete
}

# Removes the NuGet download cache entries not written in the last 30 days. NuGet itself treats an entry as stale after half an hour.
vm_prune_nuget_http() {
  local dir="$1"
  [ -d "$dir" ] || return 0
  find "$dir" -type f -mtime +30 -delete
  find "$dir" -mindepth 1 -type d -empty -delete
}

# vm_prune_caches <dotnet folder> <nuget packages folder> <nuget http cache folder>
# Called as the last step of a run, with the run's locks still held, so it never races a build in the same folder; each folder has
# its own caches, so a run in the other folder is not touched. A failure here never changes the run's result.
vm_prune_caches() {
  local before after
  before="$(du -sm "$1" "$2" "$3" 2>/dev/null | awk '{ sum += $1 } END { print sum + 0 }')" || before="?"
  vm_prune_dotnet "$1" || true
  vm_prune_nuget_packages "$2" || true
  vm_prune_nuget_http "$3" || true
  after="$(du -sm "$1" "$2" "$3" 2>/dev/null | awk '{ sum += $1 } END { print sum + 0 }')" || after="?"
  echo "==> pruned the caches: ${before} MB before, ${after} MB after"
}
