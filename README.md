# SavePoint

**Quicksave for your whole game folder.**

You spent a weekend getting 140 mods to load in the right order. The game runs beautifully. Then a patch lands overnight, half the mods break, and your 60-hour save won't load.

SavePoint is the undo button for that morning. It takes snapshots of the game's files and your saves together, so you can go back to the exact setup that worked, keep a vanilla install and a modded one side by side, or try that one sketchy mod knowing you can take it all back. It works with any game and any mod manager, because it never needs to know how a game stores its mods. It just remembers what was in the folders.

> **Status:** pre-release. Windows and Linux builds work; macOS hasn't been tried on real hardware yet.

## How it plays

1. **Add a game.** Point SavePoint at the game's folder and its saves folder. Steam games get their cover art.
2. **Take a snapshot.** That's your checkpoint: the game version, the mods, and the saves, as they are right now.
3. **Press Play in SavePoint.** It puts the right files in place, starts the game, and waits. When you quit, it asks what to do with the session: keep it as a new snapshot, keep it as a new profile, or decide later.

That's the loop. Everything else is about having more than one checkpoint.

## What you can do with it

- **Run vanilla and modded side by side.** Each profile is its own line of play, like "Vanilla 1.5" and "Survival Overhaul 1.6". Switching between them swaps the files; you never reinstall anything.
- **Roll back a bad patch.** History lists every snapshot. Pick the one from before the update, press **Go back to this**, and play.
- **Try things without fear.** Take a snapshot, install the risky mod, play. Didn't work out? Go back. Snapshots a profile moves past aren't thrown away either: they wait under **Recently removed** until you clear them, and you can bring one back as a new profile.
- **Branch your playthrough.** Sided with the wrong faction? Go back to the snapshot before the choice and keep the new session as its own profile. Both stories stay playable.
- **See what's happening.** Activity shows every job (a snapshot, a switch, freeing up space) with its progress and details, and most can be stopped.
- **Keep the disk in check.** Files are stored once, so a snapshot that only changes a few saves costs little. **Free up space** shows what it will delete before it deletes anything.

New to all this? Press **F1** for a short illustrated guide, take the tour, or hit any "What's this?" button. No knowledge of version control needed.

## Playing it safe

SavePoint touches only the folders you give it, and it's careful with them:

- It refuses whole drives and system folders, and asks before it takes over a folder.
- It never switches over new changes in the folders. You keep them, discard them, or take a snapshot first.
- Anything that can't be undone says so, and asks a second time.
- When something goes wrong, it tells you what happened and what to do next, in plain words.

Two things it can't help with:

- **Games with kernel-level anti-cheat.** Don't point SavePoint at them.
- **Cloud saves.** Turn off Steam Cloud or GOG Galaxy sync for a game SavePoint manages, or the two will keep overwriting each other.

Everything stays on your computer. The only network request SavePoint ever makes is the optional artwork lookup on Steam, which you can turn off in Settings.

## Install

Grab a package from the latest release:

- **Windows:** run `SavePoint-win-Setup.exe` (installs for your user, adds a Start menu shortcut), or unzip `SavePoint-win-Portable.zip` anywhere and run `SavePoint.exe`. The packages aren't code-signed yet, so SmartScreen asks before the first run.
- **Linux:** `chmod +x SavePoint.AppImage` and run it.
- **macOS:** not packaged yet.

SavePoint never phones home for updates. To update, run a newer setup over the old one. Uninstalling removes the app but keeps your snapshots and settings; to remove those too, use **Settings > Remove all data** first.

## Under the hood

SavePoint is a native C# app (.NET 10 and Avalonia) that runs on Windows, Linux, and macOS from one build. Snapshots are stored with git, the tool programmers use to track code, so they're compact and every file comes back byte for byte. SavePoint bundles its own pinned copy and never uses one installed on your machine, and you never see a git word in the app.

To build it yourself, you need the .NET 10 SDK (and PowerShell 7 on Windows):

```bash
dotnet build Savepoint.slnx -c Release
```

```bash
dotnet run --project src/Savepoint.App -c Release
```

The first build downloads the pinned git bundle and checks its SHA-256.

## Documentation

- [User guide](docs/user-guide.md): every screen and action, for players.
- [Developer guide](docs/developer-guide.md): building, testing, gates, and packaging.
- [Architecture](docs/architecture.md) and [Decisions](docs/decisions.md): how it's built and why.
- [UI guide](docs/ui-guide.md): how to build a screen.
- [Testing on macOS and Linux](docs/testing-on-mac-and-linux.md): the gate and the manual checks on a Mac or a Linux desktop.
- [AGENTS.md](AGENTS.md): the working rules for every change.

Planned work lives in the issues and milestones, and the manual dogfood checklist is on the wiki.

## License

SavePoint is free software: you can redistribute it and modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version ([LICENSE](LICENSE), SPDX `GPL-3.0-or-later`). It comes with no warranty.

The bundled git is licensed under GPLv2 and runs as a separate program. Its license texts, the other third-party notices, and the source offer ship next to the app ([THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), `licenses/`).
