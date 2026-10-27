#!/bin/bash
# Build The Big Board and deploy DLL + Locales to ModsLocal.
# Usage: ./deploy.sh   (run from anywhere; paths per AGENTS.md)
set -u
ROOT="$(git rev-parse --show-toplevel 2>/dev/null)"
STEAMDATA=~/.steam/debian-installation/steamapps/compatdata/1331550/pfx/drive_c/users/steamuser/AppData/LocalLow/Hovgaard\ Games/Big\ Ambitions
DEST="$STEAMDATA/ModsLocal/0000000000 The Big Board"
cd "$ROOT/src/TheBigBoard" && dotnet build || exit 1
mkdir -p "$DEST/Locales"
cp bin/Debug/netstandard2.1/TheBigBoard.dll "$DEST/"
cp Locales/*.json "$DEST/Locales/"
md5sum bin/Debug/netstandard2.1/TheBigBoard.dll "$DEST/TheBigBoard.dll"
echo "Deployed. Folder contents:"; find "$DEST" -type f | sort
