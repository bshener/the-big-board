#!/bin/bash
# Pre-publish consistency check for a release. Fails non-zero on any miss.
# Usage: ./release-check.sh <version>   (e.g. ./release-check.sh 0.11.0)
set -u
VER="${1:?usage: release-check.sh <version>}"
ROOT="$(git rev-parse --show-toplevel 2>/dev/null)"
FAIL=0
chk() { # chk <description> <command...>
  local desc="$1"; shift
  if "$@" >/dev/null 2>&1; then echo "ok: $desc"; else echo "MISS: $desc"; FAIL=1; fi
}
LOG=~/.steam/debian-installation/steamapps/compatdata/1331550/pfx/drive_c/users/steamuser/AppData/LocalLow/Hovgaard\ Games/Big\ Ambitions/Player.log
CSPROJ="$ROOT/src/TheBigBoard/TheBigBoard.csproj"
chk "csproj Version=$VER" grep -q "<Version>$VER</Version>" "$CSPROJ"
chk "ModPanel ModVersion=$VER beta" grep -q "ModVersion = \"$VER beta\"" "$ROOT/src/TheBigBoard/ModPanel.cs"
chk "boot log line=$VER" grep -q "The Big Board $VER (beta) loaded" "$ROOT/src/TheBigBoard/TheBigBoard.cs"
chk "store-page version note=$VER" grep -q "Release version: $VER (beta)" "$ROOT/docs/store-page.md"
chk "README version=v$VER beta" grep -q "v$VER beta" "$ROOT/README.md"
chk "announcements mention $VER (Patreon draft REQUIRED)" grep -q "$VER" "$ROOT/docs/announcements.md"
chk "no PENDING screenshots in shot list" bash -c "! grep -q PENDING \"$ROOT/docs/store-page.md\""
chk "git tree clean" bash -c "[ -z \"\$(git -C \"$ROOT\" status --short)\" ]"
chk "Player.log shows $VER loaded" grep -a -q "The Big Board $VER (beta) loaded" "$LOG"
chk "Player.log shows panel built" grep -a -q "panel built" "$LOG"
if [ "$FAIL" = 0 ]; then echo "ALL CHECKS PASSED"; else echo "RELEASE-CHECK FAILED"; exit 1; fi
