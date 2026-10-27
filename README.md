# The Big Board

Business command panel for **Big Ambitions**: every building, business,
employee, production line, sale and stock movement in a single phone app,
sitting alongside the vanilla apps. *"Your empire, on one board."*

- 🏢 **BUILDINGS** — census of every known building, with filters and sorting
- 🏪 **BUSINESSES** — per-shop health checklist, one-click hiring, dead-hours analysis, schedule copy
- 🧑‍💼 **EMPLOYEE** — one-click HR assign + crew recruiting
- 🏭 **FACTORY** — production plan + supply coverage
- 💰 **SALES** — per-store search
- 📦 **STOCK** — live stock with IN/OUT flows

## Install

- **Steam Workshop:** [subscribe](https://steamcommunity.com/sharedfiles/filedetails/?id=3827879897), then enable the mod from the in-game mod menu.
- **Nexus Mods:** [download](https://www.nexusmods.com/bigambitions/mods/35), drop it into your local mods folder.

Current version: **v0.9.0 beta** — bug reports welcome, every one gets read.

## Support

Free forever — no paid features, no "Pro" split. If it saves you time,
every contribution goes straight back into new projects.

<a href="https://buymeacoffee.com/bshener"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me a Coffee" height="60" /></a>
<a href="https://www.patreon.com/bshener"><img src="https://c5.patreon.com/external/logo/become_a_patron_button@2x.png" alt="Become a Patron" height="60" /></a>

## For developers

- Engine: **Unity Mono**. Official mod API `BAModAPI` — no MelonLoader/Harmony. Mods target **netstandard2.1**.
- Build: `cd src/TheBigBoard && dotnet build` (override the game path with
  `-p:BigAmbitionsManaged="<game dir>/Big Ambitions_Data/Managed"`).
- Deploy + test: see `docs/dev-loop.md`; verify in `Player.log`.

## License

Code in this repo is [MIT](LICENSE). Game screenshots in `docs/screenshots/`
show Big Ambitions UI for mod documentation purposes only.

Unofficial fan mod — not affiliated with or endorsed by Hovgaard Games.
