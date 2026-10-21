#!/usr/bin/env bash
# Builds the Linux (linux-x64) package: a self-contained publish, then vpk pack, which makes the AppImage.
#
#   build/package.sh [--sources]
#
# Publishes src/Savepoint.App into artifacts/publish/linux-x64, checks that the git bundle, the notices and the license
# texts came along, and packs it with the pinned vpk (a local tool, never a global one) into artifacts/release/linux-x64,
# with the 512 px PNG as the icon. The version is the one <Version> in Directory.Build.props.
#
# --sources  also downloads the source archives of the three git tags THIRD-PARTY-NOTICES.md names into
#            artifacts/release/source and prints their SHA-256.
#
# Run it from a checkout that only Linux builds: Windows and Linux builds of one tree share obj/, and each restore
# overwrites the other's. Under WSL, keep the tree on the Linux file system (not /mnt/c) for speed. Without a .NET SDK on the
# PATH it installs one into .tools/dotnet-linux, as build/gate-linux.sh does.
#
# Windows has its own script, build/package.ps1. macOS is not produced here: vpk has to run on a Mac, and the result has to
# be signed and notarized before anyone else can open it.
set -euo pipefail

sources=0
for argument in "$@"; do
  case "$argument" in
    --sources) sources=1 ;;
    *) echo "package: unknown argument $argument" >&2; exit 2 ;;
  esac
done

fail() {
  echo "package: $*" >&2
  exit 1
}

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"

# Builds, vpk and the downloads all inherit this, so packaging never starves the machine it runs on.
renice -n 10 $$ >/dev/null 2>&1 || true

tools="$repo/.tools"
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
rid=linux-x64
publish_dir="$repo/artifacts/publish/$rid"
release_dir="$repo/artifacts/release/$rid"
icon="$repo/src/Savepoint.App/Assets/Icon/savepoint-512.png"
echo "==> SavePoint $version for $rid, with .NET SDK $(dotnet --version)"

echo "==> restore tools"
dotnet tool restore

rm -rf "$publish_dir"
build_nodes="${SAVEPOINT_PACKAGE_BUILD_NODES:-4}"
echo "==> publish"
dotnet publish src/Savepoint.App -c Release -r "$rid" --self-contained -o "$publish_dir" "-m:$build_nodes" -nodeReuse:false -p:UseSharedCompilation=false

echo "==> check the publish folder"
for expected in SavePoint THIRD-PARTY-NOTICES.md git/.savepoint-git-stamp; do
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
dotnet vpk --skip-updates pack --packId SavePoint --packVersion "$version" --packTitle SavePoint --packDir "$publish_dir" --mainExe SavePoint --icon "$icon" --runtime "$rid" --exclude '(?i)\.pdb$|(^|[/\\])savepoint(\.core)?\.xml$' --outputDir "$release_dir"

echo "==> packages in $release_dir"
for file in "$release_dir"/*; do
  [ -f "$file" ] && printf '%14s bytes  %s\n' "$(wc -c <"$file" | tr -d ' ')" "$(basename "$file")"
done

if [ "$sources" -eq 1 ]; then
  source_dir="$repo/artifacts/release/source"
  mkdir -p "$source_dir"
  # The tags the notices promise source for: "<repository URL>, tag `<tag>`".
  tags="$(grep -oE 'https://github\.com/[A-Za-z0-9._-]+/[A-Za-z0-9._-]+, tag `[^`]+`' THIRD-PARTY-NOTICES.md || true)"
  [ "$(printf '%s\n' "$tags" | grep -c .)" -eq 3 ] || fail "THIRD-PARTY-NOTICES.md does not name exactly 3 source tags (git, Git for Windows, dugite-native)"
  while IFS= read -r line; do
    repository="${line%%,*}"
    tag="${line#*tag \`}"
    tag="${tag%\`}"
    name="$(printf '%s' "${repository#https://github.com/}" | tr '/' '-')-$tag.tar.gz"
    echo "==> $repository at $tag"
    download "$repository/archive/refs/tags/$tag.tar.gz" "$source_dir/$name"
    printf '%s  %s\n' "$(sha256sum "$source_dir/$name" | cut -d' ' -f1)" "$name"
  done <<<"$tags"
fi

echo "package done"
