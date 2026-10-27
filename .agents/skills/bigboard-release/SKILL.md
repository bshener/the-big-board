---
name: bigboard-release
description: Publish a new The Big Board release (Big Ambitions mod). Use when cutting a beta version, updating Workshop/Nexus/GitHub, or writing release announcements. Covers version bumps, docs sync, build/deploy/verify, and per-channel publishing with approval gates.
---

# The Big Board — release runbook

This repo ships QoL mods for Big Ambitions (Steam 1331550, Unity Mono,
official BAModAPI, netstandard2.1, LangVersion <= 10). Hard rules from
`AGENTS.md` apply: game assemblies are `Private=false` references (never
copied next to the DLL), never commit `bin/`/`obj/`, every table derives
from `TablePanel<TRow>`, small focused commits in present tense.

A release is NOT done until: (1) the DLL is verified in `Player.log`,
(2) a Patreon update-post exists, (3) the store-page version note matches.

## 0. Ask the user first (4 questions, in their language)

1. Version number (recommend minor bump for feature work, e.g. 0.10.0 beta).
2. New supporter names (Patreon/BMC — if none, `Supporters` stays empty).
3. Who captures the new screenshot (user's late-game save is ideal).
4. Announcement depth (full EN drafts vs short notes).

## 1. Version bump — run `scripts/bump-version.sh <new>`

It updates all call sites together (a missed one shipped 0.9.0 text in
the 0.10.0 DLL once — never bump by hand):

- `src/TheBigBoard/TheBigBoard.csproj` (`<Version>`)
- `src/TheBigBoard/ModPanel.cs` (`ModVersion`, shown in panel header)
- `src/TheBigBoard/TheBigBoard.cs` (load log line)
- `README.md` (version line + feature bullets)

`Supporters` in `ModPanel.cs` is synced from Patreon/BMC lists every
release (tiers buy recognition only, never features).

## 2. Docs

- `docs/store-page.md`: feature bullets + `Release version:` note.
- `docs/screenshots/`: new shot (real late-game save, English UI,
  1920px+), shot list updated (no PENDING entries at release).
- `docs/announcements.md`: Workshop changelog + Patreon post (REQUIRED)
  + forum reply + Reddit drafts. All public text stays English.

## 3. Build, deploy, verify

Run `scripts/deploy.sh` (builds, copies DLL + `Locales/*.json` to
`ModsLocal/0000000000 The Big Board/`, md5-checks). DLL changes need a
game restart: warn first (killing the game loses unsaved progress —
user may be mid-test), run `./scripts/restart-game.sh` (returns
immediately; NEVER wait on the game process), then check
`pgrep -f '[B]ig Ambitions.exe'` + `Player.log`.

Required `Player.log` markers before claiming anything works:

```text
The Big Board <ver> (beta) loaded.
panel built.
best sellers scan: day <n>, ...
```

plus no errors/exceptions. The full panel scan only appears after a
save is loaded and the phone UI opened — the user tests this on their
save; treat bootstrap-only as partial verification.

## 4. Publishing — one channel at a time, explicit approval each

NEVER upload to Workshop/Nexus or submit any post without the user's
step-by-step approval. Run `scripts/release-check.sh <ver>` first.

- **Workshop (in-game uploader):** verify local folder = DLL +
  `Locales/` + `thumbnail.jpg` only. The user opens EDIT MOD with
  their real mouse (synthetic xdotool clicks do NOT work on buttons
  that open external flows: EDIT MOD, MOD TOOLS, FORUM — in-panel
  navigation works; use global coords, never `--window`). Select the
  `0000000000 The Big Board` folder, paste the changelog (clipboard,
  not typing), UPDATE MOD.
- **Steam text + gallery (browser):** description via `itemedittext`
  page; gallery via `managepreviews`. The embedded browser CANNOT open
  native file pickers — the user uploads images by hand (30s job).
  `managepreviews` only touches gallery images, never the preview.
- **Patreon:** creator account required (check login identity first).
  Fill title + body, screenshot-verify, publish only on approval.
- **Forum:** find the `[MOD] The Big Board` thread via search, reply.
- **Nexus:** ship the ZIP (DLL + `Locales/`); user uploads by hand or
  via API key. Old version auto-archives.
- **GitHub:** `scripts/github-release.sh <ver>` (tag `v<ver>-beta`,
  pre-release + asset ZIP, mirroring previous releases).

## 5. Commit

One focused commit per change (`git add -A && git commit -m`), present
tense, never `bin/`/`obj/`. Release commits so far: version bump, store
page, log-line fixes, announcement drafts, screenshot, README.
