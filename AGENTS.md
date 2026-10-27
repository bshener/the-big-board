# AGENTS.md — the-big-board

> Project skeleton and rules for AI agents and humans. Details live in
> `docs/` — keep this file lean.

## Goal

Quality-of-life mods for **Big Ambitions** (Hovgaard Games).

## Environment


| Item                            | Path / version                                                                             |
| ------------------------------- | ------------------------------------------------------------------------------------------ |
| Game dir (Steam, AppID 1331550) | `~/.steam/debian-installation/steamapps/common/Big Ambitions/`                     |
| Game assemblies                 | `.../common/Big Ambitions/Big Ambitions_Data/Managed/`                                     |
| Proton prefix                   | `~/.steam/debian-installation/steamapps/compatdata/1331550/pfx/`                   |
| Local mods dir                  | `.../pfx/drive_c/users/steamuser/AppData/LocalLow/Hovgaard Games/Big Ambitions/ModsLocal/` |
| Game log                        | `.../Hovgaard Games/Big Ambitions/Player.log`                                              |
| Launch                          | `steam -applaunch 1331550` (native Steam client must be running)                           |
| .NET SDK                        | 8.0.426                                                                                    |


Key facts: Unity **Mono** (not IL2CPP). Official API `BAModAPI` in
`BigAmbitions.ModAPI.dll` — no MelonLoader/Harmony. Mods target
**netstandard2.1**. Details: `docs/modapi.md`.

## Repo layout

```
AGENTS.md               this file (EN)
README.md               human overview (EN)
docs/modapi.md          ModAPI surface + discovery rules (EN)
docs/phone-button.md    top-bar icon pattern (EN)
docs/dev-loop.md        build/deploy/reload cycle (EN)
docs/store-page.md       Workshop/Nexus/forum copy + screenshot shot list (EN)
docs/announcements.md   release announcement drafts (EN)
docs/screenshots/       store art + keyart production notes (EN)
docs/branding.md         locked name/tagline/version/handles (EN)
src/TheBigBoard/       The Big Board mod (netstandard2.1)
scripts/restart-game.sh stop + relaunch the game for testing
```

## Build &amp; test

```bash
cd src/TheBigBoard && dotnet build
# deploy: see docs/dev-loop.md
# verify: grep -a -i mod ".../Hovgaard Games/Big Ambitions/Player.log"
```

Rules:

- Game assemblies referenced with `<Private>false</Private>` — never copy
them next to the mod.
- `LangVersion` ≤ 10; `Task.CompletedTask` over async where trivial.
- Never claim a mod works without `Player.log` evidence.
- DLL changes need a game restart: run `./scripts/restart-game.sh` after
deploying a DLL change (the running game discovers new mods live but does
not reload changed DLLs). Batch related changes before relaunching
(~1 min). The script launches the game detached and returns immediately
— never `exec`/wait for the game process; verify with `pgrep` +
`Player.log`.
- DataGrid rule: every screen/panel showing a table must derive its table
from `TablePanel<TRow>` (`src/TheBigBoard/TablePanel.cs`) — never hand-roll
header/sort/resize/render in the panel. `DataGrid` owns the primitives;
the table owns headers + `CompareRows` + `RenderRow`; the panel keeps only
chrome (filters/pills/tabs) + data fetching.
- Commit after every change: `git add -A && git commit -m "<what+why>"`.
Small focused commits, present tense. Never commit `bin/`/`obj/`.
- Supporter obligations (Patreon/BMC tiers — hard rules). Tiers sell
recognition + influence, NEVER features: the mod is identical for
everyone, no paywalled tabs, no early-access builds.
  - `Supporter` ($3): name in credits + early news.
  - `Insider` ($5): everything in Supporter + vote on what's next
  (7th tab, new features, new mods).
  - Credits list lives in `ModPanel.cs` (`Supporters` array +
  `ModVersion`, rendered in the panel header). Every release MUST sync
  names from the Patreon/BMC supporter lists and bump `ModVersion`
  together with the csproj `Version`. Never ship with a stale list.
  - A release is NOT done until: DLL verified in `Player.log`, a Patreon
  update-post draft exists, and the store-page version note matches.
  - When the user calls for a vote, run it as a Patreon poll post,  
  implement the winner and announce the result in a post.