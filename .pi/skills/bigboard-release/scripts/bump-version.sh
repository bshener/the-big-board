#!/bin/bash
# Bump The Big Board to a new version across ALL call sites.
# Usage: ./bump-version.sh <new-version>   (e.g. ./bump-version.sh 0.11.0)
# A missed call site once shipped stale version text inside the DLL,
# so this script updates every site and then verifies nothing stale remains.
set -u
NEW="${1:?usage: bump-version.sh <new-version>  (e.g. 0.11.0)}"
ROOT="$(git rev-parse --show-toplevel 2>/dev/null)"
CSPROJ="$ROOT/src/TheBigBoard/TheBigBoard.csproj"
MODPANEL="$ROOT/src/TheBigBoard/ModPanel.cs"
BOOT="$ROOT/src/TheBigBoard/TheBigBoard.cs"
README="$ROOT/README.md"

OLD_VER="$(grep -o '<Version>[^<]*' "$CSPROJ" | sed 's/<Version>//')"
echo "Bumping $OLD_VER -> $NEW"
sed -i "s|<Version>$OLD_VER</Version>|<Version>$NEW</Version>|" "$CSPROJ"
sed -i "s|const string ModVersion = \".*\";|const string ModVersion = \"$NEW beta\";|" "$MODPANEL"
sed -i "s|The Big Board .* (beta) loaded.|The Big Board $NEW (beta) loaded.|" "$BOOT"
sed -i "s|Current version: \*\*v.* beta\*\*|Current version: **v$NEW beta**|" "$README"

echo "--- verifying ---"
grep -n "$NEW" "$CSPROJ" "$MODPANEL" "$BOOT" "$README" | head -8
STALE="$(grep -rn "$OLD_VER" "$ROOT/src/TheBigBoard/" "$ROOT/README.md" 2>/dev/null | grep -v 'bin/\|obj/' || true)"
if [ -n "$STALE" ]; then
  echo "STALE REFERENCES TO $OLD_VER REMAIN:"; echo "$STALE"; exit 1
fi
echo "OK: no stale $OLD_VER references (src + README)."
