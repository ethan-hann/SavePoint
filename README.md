# SavePoint

SavePoint keeps a modded game playable exactly as you set it up, while the game itself keeps getting updates. It takes snapshots of the game's files and your saves together. You can switch between setups, go back to any earlier snapshot, and pick up where you left off, and SavePoint never needs to know how a game stores its mods.

You keep your game where it is. SavePoint keeps each snapshot in its own storage, and when you switch, it puts the right files back in the game's folders before the game starts.

**Status:** pre-release, in active development. This is v2, a native rewrite of the unreleased Electron v1 (see `docs/decisions.md`).

## What it does

- **Profiles.** Each profile is its own line of play, for example "Vanilla 1.5" and "Modded 1.6". Each keeps its own history of the game's folder and of your saves folder.
- **Snapshots.** A snapshot records what is on disk now: the game version and mods, the saves, or both. Files are stored once, so a snapshot that changes only a few files costs little space. **Take a snapshot now** records one at any time.
- **Play from SavePoint.** Pick a profile or a snapshot and press Play. SavePoint shows exactly what it will change, switches the folders, starts the game (a program or a Steam link), and follows it. When you finish, it asks what to do with the session: keep it as a new snapshot, keep it as a new profile, continue the profile from it, or decide later.
- **Go back any time.** History lists every snapshot, as a list or as cards, and **Go back to this** starts the game from any of them. Snapshots a profile moves past are kept under **Recently removed**, where you can bring one back as a new profile.
- **Activity.** Every job, such as a snapshot, a switch, or freeing up space, shows its progress and details there, and most can be stopped.
- **Help built in.** Press F1 for a short illustrated guide, take the tour, or use the "What's this?" buttons. No knowledge of version control is needed.
- **Your storage, your call.** See how much space each game uses, **Free up space**, move SavePoint's storage to another drive, or remove all of SavePoint's data. None of these touch your game's own files.
- **Careful by default:**
  - It refuses whole drives and system folders, and asks before it takes over a folder.
  - It never switches over new changes: you keep them, discard them, or take a snapshot first.
  - Anything that can't be undone says so and needs a second confirmation.
  - Every failure explains what happened and what to do.
- **Game artwork:** Steam games get their cover art, from the Steam client's local cache or from Steam's servers. Other games show a tint based on their name. Your own image always wins.
- **Comfortable to use:**
  - Light and dark themes with five accent colors.
  - Sidebar and library layouts.
  - Full keyboard use, screen-reader names and announcements, and reduced motion when the system asks for it.
  - Notifications when a long job finishes while SavePoint is in the background.
- **Cross-platform.** One build for Windows, Linux, and macOS. macOS has not yet been tested on real hardware.

SavePoint bundles its own pinned copy of git and never uses one installed on your machine. Everything stays on your computer. The only network request it ever makes is the optional artwork download from Steam, which you can turn off in Settings.

Not supported: games with kernel-level anti-cheat. Turn off a storefront's cloud saves (Steam Cloud, GOG Galaxy) for a game SavePoint manages, or the two will overwrite each other.

## Installing

Packages are attached to each release on the repository's releases page.

- **Windows:** run `SavePoint-win-Setup.exe`. It installs for your user only and adds a Start menu shortcut. Or unzip `SavePoint-win-Portable.zip` anywhere and run `SavePoint.exe`. The packages are not code-signed yet, so Windows SmartScreen asks before the first run.
- **Linux:** make `SavePoint.AppImage` executable and run it.
- **macOS:** not packaged yet.

SavePoint never checks for updates on its own. To update, run a newer setup over the old install. Uninstalling removes the app but keeps your snapshots and settings; to remove those too, use **Settings > Remove all data** first.

## Building from source

You need the .NET 10 SDK; on Windows, also PowerShell 7.

```bash
dotnet build Savepoint.slnx -c Release
```

```bash
dotnet run --project src/Savepoint.App -c Release
```

The first build downloads the pinned git bundle and checks its SHA-256. Running the tests, the gates, and the rules for changes are covered in [docs/developer-guide.md](docs/developer-guide.md).

## Documentation

- [User guide](docs/user-guide.md): how to use SavePoint, for players.
- [Testing on macOS and Linux](docs/testing-on-mac-and-linux.md): the gate and the manual checks on a Mac or a Linux desktop.
- [Developer guide](docs/developer-guide.md): building, testing, gates, and the rules for changes.
- [Architecture](docs/architecture.md): the layers and what each one promises.
- [Decisions](docs/decisions.md): every departure from, and gap-fill of, the v1 spec, with reasons.
- [UI guide](docs/ui-guide.md): how to build a screen.
- [AGENTS.md](AGENTS.md): the working rules for every change.

Planned work is tracked in the repository's issues and milestones, and the manual dogfood checklist is on the wiki.

## License

SavePoint is free software: you can redistribute it and modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version ([LICENSE](LICENSE), SPDX `GPL-3.0-or-later`). It comes with no warranty. The bundled git is licensed under GPLv2 and runs as a separate program. The packages ship its license texts, the other third-party notices, and the source offer next to the app ([THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), `licenses/`, D21).
