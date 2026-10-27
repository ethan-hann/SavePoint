#!/usr/bin/env bash
# Prints the footer of the documentation site as one line of HTML: the version the guides are written for, the commit and time
# of the build, the copyright and the license.
#
# Usage: build/docs-footer.sh <repository-root> <commit> <server-url> <repository>
#
#   <server-url> and <repository> are the parts the runner already knows (GITHUB_SERVER_URL, GITHUB_REPOSITORY), for example
#   https://git.example.org and owner/name. The release and commit links are built from them.
#
# The version is read from Directory.Build.props as text, so no build has to run first. The time and the year are the commit's, in UTC,
# so the footer says the same thing wherever the site is built. It prints nothing and fails when the version or the commit is missing:
# a footer that guesses would put a wrong version on the site.
set -euo pipefail

# Bash 5.2 reads an & in a replacement as the matched text, which would break the escaping below.
shopt -u patsub_replacement 2>/dev/null || true

fail() {
  echo "docs-footer: $*" >&2
  exit 1
}

[ $# -eq 4 ] || { echo "usage: docs-footer.sh <repository-root> <commit> <server-url> <repository>" >&2; exit 2; }
root="$1"
commit="$2"
server="$3"
repository="$4"
while [[ $server == */ ]]; do server="${server%/}"; done

# Everything that reaches the page goes through here, the URLs included: they come from the runner's environment.
html_escape() {
  local text="$1" quote='"' apostrophe="'" amp='&amp;' lt='&lt;' gt='&gt;' quot='&quot;' apos='&#39;'
  text="${text//&/$amp}"
  text="${text//</$lt}"
  text="${text//>/$gt}"
  text="${text//$quote/$quot}"
  text="${text//$apostrophe/$apos}"
  printf '%s' "$text"
}

props="$root/Directory.Build.props"
[ -f "$props" ] || fail "$props does not exist."
version_pattern='<Version>[[:space:]]*([^<[:space:]]+)[[:space:]]*</Version>'
version=""
while IFS= read -r line || [ -n "$line" ]; do
  if [[ $line =~ $version_pattern ]]; then
    version="${BASH_REMATCH[1]}"
    break
  fi
done < "$props"
[ -n "$version" ] || fail "$props has no <Version> element."

full="$(git -C "$root" rev-parse --verify --quiet "$commit^{commit}")" || fail "$commit is not a commit of $root."
short="$(git -C "$root" rev-parse --short "$full")"
# One call gives the commit time in UTC as YYYY-MM-DDTHH:MM.
moment="$(TZ=UTC git -C "$root" log -1 --no-show-signature --format=%cd --date='format-local:%Y-%m-%dT%H:%M' "$full")"
[[ $moment =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}$ ]] || fail "could not read the time of $full."
year="${moment%%-*}"
shown="${moment%T*} ${moment#*T} UTC"

base="$server/$repository"
release_link="$(html_escape "$base/releases/tag/v$version")"
commit_link="$(html_escape "$base/commit/$full")"
version="$(html_escape "$version")"
short="$(html_escape "$short")"

printf '%s' '<span class="sp-footer">'
printf '<span class="sp-footer-item">Written for SavePoint <a href="%s">%s</a></span> ' "$release_link" "$version"
printf '<span class="sp-footer-item">Updated <time datetime="%sZ">%s</time> from <a href="%s">%s</a></span> ' "$moment" "$shown" "$commit_link" "$short"
printf '<span class="sp-footer-item">Copyright &copy; %s Ethan Hann</span> ' "$year"
printf '%s\n' '<span class="sp-footer-item">SavePoint is free software under the GNU GPL, version 3 or later.</span></span>'
