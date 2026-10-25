#!/usr/bin/env bash
# Builds the Linux (linux-x64) package: a self-contained publish, then vpk pack, which makes the AppImage.
#
#   build/package.sh [--sources] [--allow-dirty] [--revision <commit>[-dirty]] [--tools-dir <folder>]
#
# Publishes src/Savepoint.App into artifacts/publish/linux-x64, checks that the git bundle, the notices and the license
# texts came along, and packs it with the pinned vpk (a local tool, never a global one) into artifacts/release/linux-x64,
# with the 512 px PNG as the icon. The version is the one <Version> in Directory.Build.props. Afterwards it opens the
# AppImage and checks that the git bundle, the notices and the license texts are inside it.
#
# --sources      also downloads the source archives of the three tags build/git-manifest.json pins (the ones
#                THIRD-PARTY-NOTICES.md promises) into artifacts/release/source, prints their SHA-256 and compares each with
#                the hash the manifest pins. This runs before anything is built, so a failed check leaves no new package. An
#                archive keeps its name only when it matches its pin. A hash that is not pinned yet, or that differs, fails the
#                run once every hash has been printed. It also downloads the source archives of the MSYS2 and MINGW packages
#                inside the Windows git bundle, which build/msys2-sources.json lists with their pinned hashes, into
#                artifacts/release/source/packages, and checks them the same way.
# --allow-dirty  packs a tree with uncommitted changes anyway, for a trial package, marked "-dirty". Without it the run is
#                refused, because a package from such a tree cannot be tied to a commit.
# --revision     the commit to put into the informational version, as 12 or more hex digits with "-dirty" after them when the
#                tree has changes. Given, the script asks git nothing: a Windows-made worktree cannot be read by Linux git, so
#                build/package-linux.ps1 asks Windows git and passes the answer in. Not combined with --allow-dirty, which is
#                the answer for a script that asks git itself.
# --tools-dir    where the .NET SDK and the NuGet caches live, <tree>/.tools when not given. build/package-linux.ps1 points it at
#                the main checkout's .tools, which the Linux gate uses too, so one SDK serves both.
#
# Run it from a checkout that only Linux builds: Windows and Linux builds of one tree share obj/, and each restore
# overwrites the other's. Under WSL, keep the tree on the Linux file system (not /mnt/c) for speed. Without a .NET SDK on the
# PATH it installs one into <tools-dir>/dotnet-linux, as build/gate-linux.sh does. From Windows, build/package-linux.ps1 does
# all of this in the Linux-only worktree, .linux-gate.
#
# Windows has its own script, build/package.ps1. macOS is not produced here: vpk has to run on a Mac, and the result has to
# be signed and notarized before anyone else can open it.
set -euo pipefail

sources=0
allow_dirty=0
given_revision=""
tools_dir=""
while [ "$#" -gt 0 ]; do
  case "$1" in
    --sources) sources=1 ;;
    --allow-dirty) allow_dirty=1 ;;
    --revision)
      [ "$#" -ge 2 ] || { echo "package: --revision needs a commit" >&2; exit 2; }
      given_revision="$2"
      shift
      ;;
    --tools-dir)
      [ "$#" -ge 2 ] || { echo "package: --tools-dir needs a folder" >&2; exit 2; }
      tools_dir="$2"
      shift
      ;;
    *) echo "package: unknown argument $1" >&2; exit 2 ;;
  esac
  shift
done
if [ -n "$given_revision" ]; then
  [[ "$given_revision" =~ ^[0-9a-f]{12,40}(-dirty)?$ ]] || { echo "package: --revision is $given_revision, which is not a commit of 12 or more hex digits, optionally followed by -dirty" >&2; exit 2; }
  [ "$allow_dirty" -eq 0 ] || { echo "package: --allow-dirty and --revision do not go together; end the revision with -dirty for a trial package" >&2; exit 2; }
fi

fail() {
  echo "package: $*" >&2
  exit 1
}

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
# shellcheck source=build/package-sources.sh
. "$repo/build/package-sources.sh"

# Builds, vpk and the downloads all inherit this, so packaging never starves the machine it runs on.
renice -n 10 $$ >/dev/null 2>&1 || true

tools="${tools_dir:-$repo/.tools}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
# Build servers and reused nodes would outlive the run.
export DOTNET_CLI_USE_MSBUILD_SERVER=0
export MSBUILDDISABLENODEREUSE=1
# Linux caches of their own, so they never mix with the Windows ones that nuget.config points at.
export HOME="$tools/home-linux"
export DOTNET_CLI_HOME="$HOME"
export NUGET_PACKAGES="$tools/nuget-linux"
export NUGET_HTTP_CACHE_PATH="$tools/nuget-http-linux"
mkdir -p "$HOME" "$NUGET_PACKAGES" "$NUGET_HTTP_CACHE_PATH"

download() {
  # download <url> <file>
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL --retry 3 --retry-delay 2 -o "$2" "$1"
  else
    wget -q --tries=3 -O "$2" "$1"
  fi
}

# global.json rolls forward to the newest feature band, so any SDK of its major.minor satisfies it.
sdk_version="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([0-9]*\.[0-9]*\)\.[0-9]*.*/\1/p' global.json | head -n 1)"
[ -n "$sdk_version" ] || fail "no sdk version in global.json"
if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks | grep -q "^$sdk_version\."; then
  dotnet_root="$tools/dotnet-linux"
  if ! [ -x "$dotnet_root/dotnet" ] || ! "$dotnet_root/dotnet" --list-sdks | grep -q "^$sdk_version\."; then
    echo "==> installing the .NET $sdk_version SDK into $dotnet_root"
    mkdir -p "$tools"
    download https://dot.net/v1/dotnet-install.sh "$tools/dotnet-install.sh"
    bash "$tools/dotnet-install.sh" --channel "$sdk_version" --install-dir "$dotnet_root" --no-path
  fi
  export DOTNET_ROOT="$dotnet_root"
  export PATH="$dotnet_root:$PATH"
fi

version="$(sed -n 's#.*<Version>\(.*\)</Version>.*#\1#p' Directory.Build.props | head -n 1 | tr -d '[:space:]')"
[ -n "$version" ] || fail "Directory.Build.props has no <Version>"
# The same pack id as the Windows setup, whose install folder must never be SavePoint's data folder (D38).
pack_id="$(sed -n 's#.*<SavepointPackId>\(.*\)</SavepointPackId>.*#\1#p' Directory.Build.props | head -n 1 | tr -d '[:space:]')"
[ -n "$pack_id" ] || fail "Directory.Build.props has no <SavepointPackId>"
rid=linux-x64
publish_dir="$repo/artifacts/publish/$rid"
release_dir="$repo/artifacts/release/$rid"
icon="$repo/src/Savepoint.App/Assets/Icon/savepoint-512.png"

# Every build is the same version, so the commit is what tells two builds apart: it goes into the informational version, which
# the startup log reads. A package from a tree with changes the commit does not hold cannot be tied to one.
if [ -n "$given_revision" ]; then
  revision="$given_revision"
else
  commit="$(git rev-parse --short=12 HEAD)" || fail "git could not say which commit this is"
  # A pipe would hide a git that failed, so its answer is taken first.
  status="$(git status --porcelain)" || fail "git could not say whether the working tree has changes"
  changes="$(printf '%s' "$status" | grep -c . || true)"
  if [ "$changes" -gt 0 ] && [ "$allow_dirty" -eq 0 ]; then
    fail "the working tree has $changes uncommitted change(s), so the package could not be tied to a commit. Commit them, or pass --allow-dirty for a trial package."
  fi
  revision="$commit"
  if [ "$changes" -gt 0 ]; then
    revision="$commit-dirty"
  fi
fi
echo "==> SavePoint $version ($revision) for $rid, with .NET SDK $(dotnet --version)"

if [ "$sources" -eq 1 ]; then
  source_dir="$repo/artifacts/release/source"
  mkdir -p "$source_dir"
  # The source archives a release publishes are the ones the manifest pins, beside the binaries they belong to. The manifest keeps
  # one "key": "value" pair per line, which is all this reads, as build/fetch-git.sh does.
  pins="$(awk '
    /^  "sources": \{/ { insources = 1; next }
    insources && /^  \}/ { exit }
    insources && /^      "repository": "/ { repository = $0; sub(/^      "repository": "/, "", repository); sub(/",?[ \t\r]*$/, "", repository) }
    insources && /^      "tag": "/ { tag = $0; sub(/^      "tag": "/, "", tag); sub(/",?[ \t\r]*$/, "", tag) }
    insources && /^      "sha256": "/ { sha = $0; sub(/^      "sha256": "/, "", sha); sub(/",?[ \t\r]*$/, "", sha); sha = tolower(sha) }
    insources && /^    \}/ { print repository " " tag " " sha; repository = tag = sha = "" }
  ' build/git-manifest.json)"
  [ "$(printf '%s\n' "$pins" | grep -c .)" -eq 3 ] || fail "build/git-manifest.json does not pin exactly 3 source archives in \"sources\" (git, Git for Windows, dugite-native)"
  problems=""
  while read -r repository tag pinned; do
    # The notices make the promise, so they have to name the same tag.
    grep -qF "$repository, tag \`$tag\`" THIRD-PARTY-NOTICES.md || fail "THIRD-PARTY-NOTICES.md does not promise source for $repository, tag \`$tag\`, which build/git-manifest.json pins"
    name="$(printf '%s' "${repository#https://github.com/}" | tr '/' '-')-$tag.tar.gz"
    echo "==> $repository at $tag"
    # The archive takes its name only when it matches its pin, so a wrong one never sits in the folder as if it were the right one.
    partial="$source_dir/$name.part"
    download "$repository/archive/refs/tags/$tag.tar.gz" "$partial"
    actual="$(sha256sum "$partial" | cut -d' ' -f1)"
    printf '%s  %s\n' "$actual" "$name"
    if [ -z "$pinned" ]; then
      problems="$problems$name has no pinned hash in build/git-manifest.json; if this download is the right one, pin $actual."$'\n'
      rm -f "$partial"
    elif [ "$pinned" != "$actual" ]; then
      problems="$problems$name is not the archive build/git-manifest.json pins: expected $pinned, got $actual."$'\n'
      rm -f "$partial"
    else
      mv -f "$partial" "$source_dir/$name"
    fi
  done <<<"$pins"
  # The packages of the Windows git bundle go in a folder of their own, so the three tag archives stay easy to find.
  package_pins="$(package_source_pins build/msys2-sources.json)" || fail "could not read build/msys2-sources.json"
  [ "$(printf '%s\n' "$package_pins" | grep -c .)" -gt 0 ] || fail "build/msys2-sources.json lists no source archives in \"archives\""
  package_dir="$source_dir/packages"
  mkdir -p "$package_dir"
  # An older list's archives would otherwise stay in the folder and be published with this release.
  remove_stale_package_sources "$package_dir" "$package_pins" || fail "could not clean $package_dir"
  while read -r file url pinned; do
    echo "==> $file"
    # An archive already in the folder was checked against its pin when it took its name, so only a changed pin makes it download again.
    if [ -f "$package_dir/$file" ] && [ "$(sha256sum "$package_dir/$file" | cut -d' ' -f1)" = "$pinned" ]; then
      printf '%s  %s\n' "$pinned" "$file"
      continue
    fi
    partial="$package_dir/$file.part"
    download "$url" "$partial"
    actual="$(sha256sum "$partial" | cut -d' ' -f1)"
    printf '%s  %s\n' "$actual" "$file"
    if [ -z "$pinned" ]; then
      problems="$problems$file has no pinned hash in build/msys2-sources.json; if this download is the right one, pin $actual."$'\n'
      rm -f "$partial"
    elif [ "$pinned" != "$actual" ]; then
      problems="$problems$file is not the archive build/msys2-sources.json pins: expected $pinned, got $actual."$'\n'
      rm -f "$partial"
    else
      mv -f "$partial" "$package_dir/$file"
    fi
  done <<<"$package_pins"
  [ -z "$problems" ] || fail "$problems"
fi

echo "==> restore tools"
dotnet tool restore

rm -rf "$publish_dir"
build_nodes="${SAVEPOINT_PACKAGE_BUILD_NODES:-4}"
echo "==> publish"
dotnet publish src/Savepoint.App -c Release -r "$rid" --self-contained -o "$publish_dir" "-m:$build_nodes" -nodeReuse:false -p:UseSharedCompilation=false "-p:SourceRevisionId=$revision"

echo "==> check the publish folder"
for expected in SavePoint THIRD-PARTY-NOTICES.md LICENSE git/.savepoint-git-stamp; do
  [ -f "$publish_dir/$expected" ] || fail "$expected is missing from $publish_dir"
done
# The stamp inside the bundle names the git executable, relative to git/.
git_entry="$(sed -n 's/^entry=//p' "$publish_dir/git/.savepoint-git-stamp" | tr -d '[:space:]')"
{ [ -n "$git_entry" ] && [ -f "$publish_dir/git/$git_entry" ]; } || fail "the git executable ($git_entry) is missing from $publish_dir/git"
[ -n "$(ls -A "$publish_dir/licenses" 2>/dev/null)" ] || fail "licenses/ is missing or empty in $publish_dir"
[ -x "$publish_dir/SavePoint" ] || fail "SavePoint in $publish_dir is not executable"

# A release is repacked from scratch: old packages would otherwise be taken for delta bases.
rm -rf "$release_dir"
# --exclude drops the debug symbols and the XML documentation files the build writes next to the assemblies. Giving it replaces vpk's default (.pdb), so the pattern repeats that.
echo "==> pack"
dotnet vpk --skip-updates pack --packId "$pack_id" --packVersion "$version" --packTitle SavePoint --packDir "$publish_dir" --mainExe SavePoint --icon "$icon" --runtime "$rid" --exclude '(?i)\.pdb$|(^|[/\\])savepoint(\.core)?\.xml$' --outputDir "$release_dir"
# vpk names the AppImage after the pack id; the one people download carries the app's name.
[ "$pack_id" = SavePoint ] || mv -f "$release_dir/$pack_id.AppImage" "$release_dir/SavePoint.AppImage"

echo "==> packages in $release_dir"
for file in "$release_dir"/*; do
  [ -f "$file" ] && printf '%14s bytes  %s\n' "$(wc -c <"$file" | tr -d ' ')" "$(basename "$file")"
done

# What vpk packed is what people install, so the AppImage is opened and the files the publish folder was checked for have to be in it.
echo "==> check the package"
appimage="$release_dir/SavePoint.AppImage"
[ -f "$appimage" ] || fail "vpk wrote no $appimage to look inside"
chmod +x "$appimage"
inspect_dir="$tools/package-check"
rm -rf "$inspect_dir"
mkdir -p "$inspect_dir"
# An AppImage unpacks itself with --appimage-extract, which needs no FUSE.
(cd "$inspect_dir" && "$appimage" --appimage-extract >/dev/null) || fail "could not look inside $appimage"
packed="$inspect_dir/squashfs-root"
listing="$release_dir/../contents-$rid.txt"
(cd "$packed" && find . -type f | LC_ALL=C sort) >"$listing"
echo "    SavePoint.AppImage: $(grep -c . "$listing") files (listed in $listing)"
grep -qE '(^|/)git/\.savepoint-git-stamp$' "$listing" || fail "SavePoint.AppImage does not hold the git bundle"
grep -qE '(^|/)THIRD-PARTY-NOTICES\.md$' "$listing" || fail "SavePoint.AppImage does not hold the notices"
# Only SavePoint's own file counts; the app lives in usr/bin inside the AppImage, and bundled components carry LICENSE files deeper down.
grep -qE '^\./usr/bin/LICENSE$' "$listing" || fail "SavePoint.AppImage does not hold the license"
grep -qE '(^|/)licenses/[^/]+$' "$listing" || fail "SavePoint.AppImage does not hold the license texts"
rm -rf "$inspect_dir"

echo "package done"
