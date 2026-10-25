#!/usr/bin/env bash
# Installs the newest .NET 10 SDK into the given folder, for CI jobs. Microsoft's channel link can name a release before its
# files can be downloaded (the origin answers 409), so when the channel install fails, the SDK the release feed lists as the
# latest is installed instead. A second argument replaces the channel, which is how the fallback is tried by hand.
set -euo pipefail

dir="$1"
channel="${2:-10.0}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

curl -fsSL --retry 3 --retry-delay 2 -o "$work/dotnet-install.sh" https://dot.net/v1/dotnet-install.sh
if bash "$work/dotnet-install.sh" --channel "$channel" --install-dir "$dir" --no-path; then
  exit 0
fi

# Saved first: grep stops reading at its match, and a curl still writing into the pipe would fail the script.
curl -fsSL --retry 3 --retry-delay 2 -o "$work/releases.json" https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json
latest=$(grep -m1 -o '"latest-sdk": *"[^"]*"' "$work/releases.json" | grep -o '[0-9][0-9.]*[0-9]')
echo "The channel's newest SDK could not be downloaded; installing $latest, the latest the release feed lists."
bash "$work/dotnet-install.sh" --version "$latest" --install-dir "$dir" --no-path
