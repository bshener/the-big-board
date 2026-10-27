# Dev loop (Steam era, from 2026-10-26)

```bash
cd src/TheBigBoard && dotnet build
STEAMDATA=~/.steam/debian-installation/steamapps/compatdata/1331550/pfx/drive_c/users/steamuser/AppData/LocalLow/Hovgaard\ Games/Big\ Ambitions
mkdir -p "$STEAMDATA/ModsLocal/0000000000 The Big Board/Locales"
cp bin/Debug/netstandard2.1/TheBigBoard.dll "$STEAMDATA/ModsLocal/0000000000 The Big Board/"
cp Locales/*.json "$STEAMDATA/ModsLocal/0000000000 The Big Board/Locales/"
tail -f "$STEAMDATA/Player.log" | grep -a -i mod
```

- The game runs via Proton under the native Steam client (`steam
  -applaunch 1331550`). The client must be running.
- DLL changes need a game restart: `./scripts/restart-game.sh` (kill
  `Big Ambitions.exe`, relaunch via Steam, ~1 min). Never exec/wait.
- `Player.log` markers: `[Mod:<id> <name>]` for runtime logs,
  `[ModDiscovery]` for scan results.
- `steam_mod_manifest.txt` in the game-data dir governs Workshop mods;
  local mods need no manifest.
