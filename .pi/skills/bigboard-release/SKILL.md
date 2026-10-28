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

NOTE (2026-10-28): `bump-version.sh`, `deploy.sh`, `release-check.sh`
and `github-release.sh` do NOT exist yet — only `restart-game.sh`
does. Until they are written, do the steps below by hand.

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

Build + deploy by hand (until `deploy.sh` exists):

```bash
cd src/TheBigBoard && dotnet build
STEAMDATA="$HOME/.steam/debian-installation/steamapps/compatdata/1331550/pfx/drive_c/users/steamuser/AppData/LocalLow/Hovgaard Games/Big Ambitions"
mkdir -p "$STEAMDATA/ModsLocal/0000000000 The Big Board/Locales"
cp bin/Debug/netstandard2.1/TheBigBoard.dll "$STEAMDATA/ModsLocal/0000000000 The Big Board/"
cp Locales/*.json "$STEAMDATA/ModsLocal/0000000000 The Big Board/Locales/"
md5sum bin/Debug/netstandard2.1/TheBigBoard.dll "$STEAMDATA/ModsLocal/0000000000 The Big Board/TheBigBoard.dll"
```

DLL changes need a game restart: warn first (killing the game loses
unsaved progress — user may be mid-test), run
`./scripts/restart-game.sh` (returns immediately; NEVER wait on the
game process), then check `pgrep -f '[B]ig Ambitions.exe'` +
`Player.log`.

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
step-by-step approval. `release-check.sh` does not exist yet — check
by hand instead: local folder = DLL + `Locales/` + `thumbnail.jpg`
only; `Player.log` has the load marker with no errors; store-page
version note matches; Patreon draft exists; no PENDING screenshots.

- **Workshop (in-game uploader):** the user opens EDIT MOD with
  their real mouse (synthetic xdotool clicks do NOT work on buttons
  that open external flows: EDIT MOD, MOD TOOLS, FORUM — but a
  synthetic click on UPDATE MOD DOES work, proven 2026-10-28).
  Paste the changelog from clipboard, not typing: write it to
  `/tmp/`, `xclip -selection clipboard`, click the Change Logs field
  (`windowactivate` the game first), `xdotool key --window <id>
  ctrl+v`, verify with screenshot, `ctrl+Home` + screenshot to verify
  the head, then click UPDATE MOD and screenshot the progress bar
  (80% → done) until it finishes.
- **Steam text + gallery (browser):** description via
  `sharedfiles/itemedittext/?id=3827879897`, gallery via
  `sharedfiles/managepreviews/?id=3827879897` — NOTE the `?id=` query
  form; the `/itemedittext/<id>` path form 404s ("item does not
  exist"). Verify an upload at
  `sharedfiles/filedetails/changelog/3827879897`. The bridge CANNOT
  open native file pickers (LibreWolf is not on the game's X display,
  so xdotool can't reach its dialogs either) — the user uploads
  images by hand (30s job). `managepreviews` only touches gallery
  images, never the preview.
- **Patreon:** creator account required (check login identity first).
  `patreon.com/posts/new` redirects to a draft edit URL — title is
  `textarea[placeholder="Title"]` (fillable), body is a ProseMirror
  `.remirror-editor` (fill via `execCommand('insertText')` —
  `press_key` cannot send Ctrl+V). Click the Publish button by exact
  text match; a bare `button` selector hits the wrong control and
  navigates away. Screenshot-verify before publishing.
- **Forum:** official forum is `forum.bigambitionsgame.com` — search
  finds `[MOD] The Big Board` (topic 7637). Reply via the composer:
  `insertText` into `.d-editor-input`, submit with the
  `#reply-control button.create` Reply button. A discard-dialog means
  the post did NOT submit — cancel it and retry.
- **Nexus:** ship the ZIP (DLL + `Locales/`); user uploads by hand
  (native picker unreachable, see above). In `edit/files`, the
  "Select existing file" dropdown lists the OLD files being
  replaced (0.11.0 will never appear there — pick the current one),
  "File version" is a free-text input (overwrite the prefilled
  value), tick HeadlessUI switches "Archive existing file" +
  "Update mod version...", add the changelog, Save file, then the
  top-level Save. Verify on the public page (`?tab=files` shows the
  new version + timestamp).
- **Reddit:** the new-UI composer (Lexical) is NOT fillable via the
  bridge — use `old.reddit.com/r/bigambitions/submit/`, switch to
  the **text** tab, and fill the title AGAIN on that tab (link/text
  tabs have separate title fields; submit fails with "title is
  required" otherwise).
- **GitHub:** `github-release.sh` does not exist yet — by hand:
  zip DLL + `Locales/` as `TheBigBoard-<ver>-beta-nexus.zip`, push
  tag `v<ver>-beta`, `gh release create v<ver>-beta <zip> --prerelease
  --title "v<ver> beta" --notes "..."`, mirroring previous
  releases.

## 5. Commit

One focused commit per change (`git add -A && git commit -m`), present
tense, never `bin/`/`obj/`. Release commits so far: version bump, store
page, log-line fixes, announcement drafts, screenshot, README.
