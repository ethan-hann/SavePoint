#!/usr/bin/env bash
# Downloads the pinned git bundle for one runtime id into resources/git/<rid>/.
#
# Usage: build/fetch-git.sh [runtime-id] [--force] [--prune-only <folder>] [--manifest <file>]
#
# Reads build/git-manifest.json, downloads the archive to .tools/downloads/, refuses it unless its
# SHA-256 matches the manifest, extracts it beside the final folder, deletes what the layout's
# "prune" list names (Git Credential Manager, Git LFS, the http transport) and renames it into place.
# A stamp file inside the bundle records version, hash, stamp format and the prune list that was applied,
# so a re-run with a matching stamp changes nothing. Safe to run from several processes at once.
#
# A prune pattern is a path relative to the bundle root, written with forward slashes. * and ? match
# within one path segment and never cross a slash; anything else is literal. A match is deleted with
# everything below it, and folders the deletion leaves empty go too. A pattern that would remove the
# entry point stops the script.
#
# --prune-only prunes an existing folder with the runtime id's list and stops (no download, no stamp).
# --manifest reads another manifest than build/git-manifest.json. Both exist for the tests.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(dirname "$script_dir")"
downloads_dir="$repo_root/.tools/downloads"
bundles_dir="$repo_root/resources/git"
stamp_name=".savepoint-git-stamp"
# Bumped when the stamp or the prune step changes, so bundles extracted by an older script are made again.
stamp_format=2

rid=""
force=0
prune_only=""
manifest="$script_dir/git-manifest.json"
while [ $# -gt 0 ]; do
  case "$1" in
    --force) force=1 ;;
    --prune-only | --manifest)
      option="$1"
      shift
      [ $# -gt 0 ] || { echo "$option needs a value" >&2; exit 2; }
      if [ "$option" = --prune-only ]; then prune_only="$1"; else manifest="$1"; fi
      ;;
    -*) echo "unknown option: $1" >&2; exit 2 ;;
    *) rid="$1" ;;
  esac
  shift
done

fail() {
  echo "fetch-git: $*" >&2
  exit 1
}

host_runtime_id() {
  local os arch
  case "$(uname -s)" in
    Linux) os=linux ;;
    Darwin) os=osx ;;
    *) fail "unsupported operating system $(uname -s); use build/fetch-git.ps1 on Windows" ;;
  esac
  case "$(uname -m)" in
    x86_64 | amd64) arch=x64 ;;
    arm64 | aarch64) arch=arm64 ;;
    *) fail "unsupported architecture $(uname -m)" ;;
  esac
  echo "$os-$arch"
}

# The manifest keeps one "key": "value" pair per line, which is all this parser relies on.
manifest_value() {
  awk -v rid="$1" -v key="$2" '
    $0 ~ ("^  \"" rid "\": \\{") { inblock = 1; next }
    inblock && /^  \}/ { exit }
    inblock {
      prefix = "^    \"" key "\": \""
      if ($0 ~ prefix) {
        sub(prefix, "")
        sub(/",?[ \t\r]*$/, "")
        print
        exit
      }
    }
  ' "$manifest"
}

sha256_of() {
  local sum
  if command -v sha256sum >/dev/null 2>&1; then
    sum="$(sha256sum "$1")"
  else
    sum="$(shasum -a 256 "$1")"
  fi
  echo "${sum%% *}" | tr 'A-F' 'a-f'
}

sha256_stdin() {
  local sum
  if command -v sha256sum >/dev/null 2>&1; then
    sum="$(sha256sum)"
  else
    sum="$(shasum -a 256)"
  fi
  echo "${sum%% *}" | tr 'A-F' 'a-f'
}

stamp_matches() {
  local stamp="$bundle_dir/$stamp_name"
  [ -f "$stamp" ] || return 1
  [ -e "$bundle_dir/$entry" ] || return 1
  grep -qxF "version=$version" "$stamp" &&
    grep -qxF "sha256=$sha256" "$stamp" &&
    grep -qxF "layout=$layout" "$stamp" &&
    grep -qxF "entry=$entry" "$stamp" &&
    grep -qxF "format=$stamp_format" "$stamp" &&
    grep -qxF "prune=$prune_hash" "$stamp"
}

# One pattern per line, between the layout's "prune": [ and its closing bracket.
manifest_prune_patterns() {
  awk -v layout="$1" '
    $0 ~ ("^    \"" layout "\": \\{") { inlayout = 1; next }
    inlayout && /^    \}/ { exit }
    inlayout && /^      "prune": \[/ { inlist = 1; next }
    inlist && /^      \]/ { exit }
    inlist {
      line = $0
      sub(/^[ \t]*"/, "", line)
      sub(/",?[ \t\r]*$/, "", line)
      print line
    }
  ' "$manifest"
}

check_prune_pattern() {
  local pattern="$1"
  case "$pattern" in
    '') fail "a prune pattern of layout '$layout' is empty" ;;
    /*) fail "prune pattern '$pattern' must be relative to the bundle" ;;
    *\\* | *:* | *\[* | *\]* | *'`'* | *[[:cntrl:]]*) fail "prune pattern '$pattern' must use / and plain characters only" ;;
  esac
  case "/$pattern/" in
    *//* | */./* | */../*) fail "prune pattern '$pattern' has an empty, . or .. segment" ;;
  esac
}

prune_bundle() {
  local dir="${1%/}" removed=0 pattern match relative parent
  while IFS= read -r pattern; do
    # The pattern is left unquoted on purpose: the shell expands it against the bundle.
    for match in "$dir"/$pattern; do
      [ -e "$match" ] || [ -L "$match" ] || continue
      relative="${match#"$dir"/}"
      case "$entry/" in
        "$relative/"*) fail "prune pattern '$pattern' would remove the entry point $entry" ;;
      esac
      rm -rf -- "$match"
      removed=$((removed + 1))
      parent="$(dirname "$match")"
      while [ "$parent" != "$dir" ] && rmdir "$parent" 2>/dev/null; do
        parent="$(dirname "$parent")"
      done
    done
  done <<EOF
$prune_patterns
EOF
  echo "Pruned $removed entries"
}

report_up_to_date() {
  # MSBuild compares the stamp's timestamp with the manifest, so a no-op run refreshes it.
  touch "$bundle_dir/$stamp_name"
  echo "git $version for $rid is up to date"
}

lock_dir=""
staging=""
release_lock() {
  [ -n "$staging" ] && rm -rf "$staging"
  [ -n "$lock_dir" ] && rm -rf "$lock_dir"
  return 0
}

acquire_lock() {
  lock_dir="$downloads_dir/$rid.lock.d"
  local waited=0 owner
  until mkdir "$lock_dir" 2>/dev/null; do
    owner="$(cat "$lock_dir/pid" 2>/dev/null || true)"
    if [ -n "$owner" ] && ! kill -0 "$owner" 2>/dev/null; then
      rm -rf "$lock_dir" # the process that held it died
      continue
    fi
    if [ -z "$owner" ] && [ -n "$(find "$lock_dir" -maxdepth 0 -mmin +10 2>/dev/null)" ]; then
      rm -rf "$lock_dir" # never got as far as writing its pid
      continue
    fi
    waited=$((waited + 1))
    [ "$waited" -le 900 ] || fail "timed out waiting for another fetch of the git bundle ($lock_dir)"
    sleep 1
  done
  echo $$ >"$lock_dir/pid"
}

download() {
  case "$url" in
    https://*) ;;
    *) fail "refusing a non-HTTPS download url: $url" ;;
  esac
  if [ -f "$archive" ] && [ "$(sha256_of "$archive")" = "$sha256" ]; then
    echo "Using cached $(basename "$archive")"
    return
  fi
  rm -f "$archive"
  echo "Downloading $url"
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL --retry 3 --retry-delay 2 -A savepoint-build -o "$archive.part" "$url"
  elif command -v wget >/dev/null 2>&1; then
    wget -q --tries=3 -U savepoint-build -O "$archive.part" "$url"
  else
    fail "neither curl nor wget is available"
  fi
  local actual
  actual="$(sha256_of "$archive.part")"
  if [ "$actual" != "$sha256" ]; then
    rm -f "$archive.part"
    fail "SHA-256 mismatch for $url: expected $sha256 but got $actual. Nothing was extracted."
  fi
  mv -f "$archive.part" "$archive"
}

extract() {
  mkdir -p "$staging"
  case "$archive_type" in
    # -p keeps the executable bits, --no-same-owner stops root runs from copying the archive's owner.
    tar.gz) tar -xpzf "$archive" -C "$staging" --no-same-owner ;;
    zip)
      if command -v unzip >/dev/null 2>&1; then
        unzip -q "$archive" -d "$staging"
      elif command -v bsdtar >/dev/null 2>&1; then
        bsdtar -xf "$archive" -C "$staging"
      elif tar --version 2>/dev/null | grep -qi bsdtar; then
        tar -xf "$archive" -C "$staging" # bsdtar reads zip, GNU tar does not
      else
        fail "unzip or bsdtar is needed to extract a zip archive"
      fi
      ;;
    *) fail "unsupported archive type '$archive_type'" ;;
  esac
}

[ -n "$rid" ] || rid="$(host_runtime_id)"
url="$(manifest_value "$rid" url)"
[ -n "$url" ] || fail "no bundled git for runtime id '$rid'"
version="$(manifest_value "$rid" version)"
sha256="$(manifest_value "$rid" sha256)"
archive_type="$(manifest_value "$rid" archive)"
layout="$(manifest_value "$rid" layout)"
entry="$(manifest_value "$rid" entry)"
for value in "$version" "$sha256" "$archive_type" "$layout" "$entry"; do
  [ -n "$value" ] || fail "incomplete manifest entry for '$rid'"
done

prune_patterns="$(manifest_prune_patterns "$layout")"
[ -n "$prune_patterns" ] || fail "the manifest has no prune list for layout '$layout'"
while IFS= read -r pattern; do
  check_prune_pattern "$pattern"
done <<EOF
$prune_patterns
EOF
prune_hash="$(printf '%s\n' "$prune_patterns" | sha256_stdin)"

if [ -n "$prune_only" ]; then
  prune_bundle "$prune_only"
  exit 0
fi

bundle_dir="$bundles_dir/$rid"
archive="$downloads_dir/$(basename "$url")"

if [ "$force" -eq 0 ] && stamp_matches; then
  report_up_to_date
  exit 0
fi

mkdir -p "$downloads_dir" "$bundles_dir"
trap release_lock EXIT
acquire_lock
staging="$bundles_dir/.$rid.staging-$$"

# Another process may have finished the same fetch while this one waited.
if [ "$force" -eq 0 ] && stamp_matches; then
  report_up_to_date
  exit 0
fi

download
rm -rf "$staging"
extract
[ -e "$staging/$entry" ] || fail "the archive for $rid does not contain $entry"
prune_bundle "$staging"
if [ "$layout" = dugite ] && [ ! -x "$staging/$entry" ]; then
  fail "$entry is not executable after extraction"
fi

# The stamp goes in last: a bundle without one is treated as unfinished.
{
  echo "version=$version"
  echo "sha256=$sha256"
  echo "layout=$layout"
  echo "entry=$entry"
  echo "format=$stamp_format"
  echo "prune=$prune_hash"
  echo "extracted=$(date -u +%Y%m%dT%H%M%SZ)-$$"
} >"$staging/$stamp_name"

rm -rf "$bundle_dir"
mv "$staging" "$bundle_dir"
staging=""
echo "Installed git $version for $rid into $bundle_dir"
