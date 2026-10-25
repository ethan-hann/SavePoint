# Functions for the source-archive step of build/package.sh. Sourced by it and by the tests, never run on its own.

_package_sources_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

package_source_pins() {
  # package_source_pins <manifest>: one "<file> <url> <sha256>" line per archive. A small .NET tool reads the JSON, since the build
  # already needs the SDK, so no layout of the file can change what is read.
  dotnet run --project "$_package_sources_root/tools/PackageSources" -c Release -v q -- archives "$1"
}

tag_source_pins() {
  # tag_source_pins <manifest>: one "<repository> <tag> <sha256>" line per entry of "sources" in build/git-manifest.json, sorted by key.
  dotnet run --project "$_package_sources_root/tools/PackageSources" -c Release -v q -- tags "$1"
}

remove_stale_package_sources() {
  # remove_stale_package_sources <folder> <pins>: deletes the regular files directly inside <folder> that <pins> (the lines of
  # package_source_pins) does not name, and says which. Links, folders and anything below a folder stay. An empty list is refused,
  # so a list that failed to read cannot empty the folder.
  local folder="$1" pins="$2" listed path name
  listed="$(cut -d' ' -f1 <<<"$pins")"
  if [ -z "$listed" ]; then
    echo "package: refusing to clean $folder against an empty list of source archives" >&2
    return 1
  fi
  [ -d "$folder" ] || return 0
  while IFS= read -r -d '' path; do
    name="${path##*/}"
    # A here-string, not a pipe: with pipefail a pipe that ends early could read as "not listed" and delete a pinned archive.
    if ! grep -Fxq -- "$name" <<<"$listed"; then
      rm -f -- "$path"
      echo "removed stale source archive: $name"
    fi
  done < <(find "$folder" -mindepth 1 -maxdepth 1 -type f -print0)
}
