#!/bin/bash
# Cut the GitHub side of a release: annotated tag + pre-release + asset ZIP.
# Usage: ./github-release.sh <version> <notes-file>
#   e.g. ./github-release.sh 0.11.0 /tmp/relnotes.txt
# The ZIP is built from the deployed ModsLocal files (verified DLL), never committed.
set -u
VER="${1:?usage: github-release.sh <version> <notes-file>}"
NOTES="${2:?usage: github-release.sh <version> <notes-file>}"
ROOT="$(git rev-parse --show-toplevel 2>/dev/null)"
[ -f "$NOTES" ] || { echo "notes file missing: $NOTES"; exit 1; }
STEAMDATA=~/.steam/debian-installation/steamapps/compatdata/1331550/pfx/drive_c/users/steamuser/AppData/LocalLow/Hovgaard\ Games/Big\ Ambitions
SRC="$STEAMDATA/ModsLocal/0000000000 The Big Board"
ZIP="/tmp/TheBigBoard-$VER-beta-nexus.zip"
rm -rf /tmp/nxrel && mkdir -p /tmp/nxrel/Locales
cp "$SRC/TheBigBoard.dll" /tmp/nxrel/ && cp "$SRC/Locales/"*.json /tmp/nxrel/Locales/
grep -a -q "$VER" /tmp/nxrel/TheBigBoard.dll || { echo "DLL does not contain $VER"; exit 1; }
rm -f "$ZIP" && (cd /tmp/nxrel && zip -q -r "$ZIP" TheBigBoard.dll Locales) && unzip -l "$ZIP"
git -C "$ROOT" tag -a "v$VER-beta" -m "The Big Board $VER beta" && git -C "$ROOT" push origin "v$VER-beta"
gh release create "v$VER-beta" --prerelease --title "v$VER beta" --notes-file "$NOTES" "$ZIP"
