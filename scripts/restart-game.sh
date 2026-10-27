#!/bin/bash
# Restart Big Ambitions (Steam, AppID 1331550) for mod testing.
# Usage: ./scripts/restart-game.sh
# The game runs via Proton under the native Steam client, which must be
# running. Launch is `steam -applaunch 1331550` (returns immediately).
set -u

# Bracket trick: the script's own cmdline contains this source line, so a
# plain "Big Ambitions.exe" pattern would match (and kill) the script itself.
PATTERN="[B]ig Ambitions\.exe"

if ! pgrep -f "$PATTERN" > /dev/null; then
  echo "Game is not running. Starting..."
else
  echo "Stopping game..."
  pkill -f "$PATTERN"
  for _ in $(seq 1 30); do
    pgrep -f "$PATTERN" > /dev/null || break
    sleep 2
  done
  if pgrep -f "$PATTERN" > /dev/null; then
    echo "Game did not exit gracefully, forcing..."
    pkill -9 -f "$PATTERN"
    sleep 3
  fi
  echo "Game stopped."
fi

if ! pgrep -f "[s]team" > /dev/null; then
  echo "Steam client is not running — start Steam first." >&2
  exit 1
fi

echo "Starting game via Steam (takes about a minute)..."
# `steam -applaunch` talks to the running client and returns at once;
# still detach defensively so the caller always gets its prompt back.
# Never exec/wait for the game here.
setsid nohup steam -applaunch 1331550 >/tmp/big-ambitions-launch.log 2>&1 < /dev/null &
disown 2>/dev/null || true
echo "Game launch requested."
echo "Verify with: pgrep -f \"$PATTERN\" and tail of Player.log."
