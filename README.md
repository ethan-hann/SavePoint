# SavePoint

> **Looking at this on GitHub?** This is a read-only mirror. SavePoint lives at [git.tortal.tech/ethan-hann/SavePoint](https://git.tortal.tech/ethan-hann/SavePoint): downloads are on its [releases page](https://git.tortal.tech/ethan-hann/SavePoint/releases), and [issues](https://git.tortal.tech/ethan-hann/SavePoint/issues), [pull requests](https://git.tortal.tech/ethan-hann/SavePoint/pulls) are there too, and the documentation is at [docs.tortal.tech/SavePoint](https://docs.tortal.tech/SavePoint/). Issues and pull requests opened on GitHub are not seen.

**Quicksave for your whole game folder.**

You spent a weekend getting 140 mods to load in the right order, and the game finally runs right. Then a patch comes down overnight, half the mods break, and your 60-hour save won't load.

SavePoint is the undo button for that morning. It takes snapshots of the game's files and your saves together, so you can go back to the exact setup that worked, keep a vanilla install and a modded one side by side, or try that one sketchy mod knowing you can take it all back. It works with any game and any mod manager, because it never needs to know how a game stores its mods. It just remembers what was in the folders.

> **Status:** pre-release. Windows and Linux builds work. macOS hasn't been tried on real hardware yet.

![A game with three profiles: a modded playthrough, the vanilla latest update, and a survival test](docs/images/games.png)

## How it plays

1. **Add a game.** Point SavePoint at the game's folder and its saves folder. Steam games get their cover art.
2. **Take a snapshot.** That's your checkpoint: the game version, the mods, and the saves, as they are right now.
3. **Press Play in SavePoint.** It puts the right files in place, starts the game, and waits. When you quit, it asks what to do with the session: keep it as a new snapshot, replace the snapshot you started from, or decide later. After you play an older snapshot, it also offers to keep the session as a new profile.

![After you quit, SavePoint asks what to do with the session](docs/images/after-play.png)

That's the whole loop. Everything else is about having more than one checkpoint.

## What you can do with it

- **Run vanilla and modded side by side.** Each profile is its own line of play, like "Vanilla 1.5" and "Survival Overhaul 1.6". Switching between them swaps the files, so you never reinstall anything.
- **Go back after a bad patch.** History lists every snapshot. Pick the one from before the update, press **Go back to this**, and play.
- **Try things without worrying.** Take a snapshot, install the risky mod, and play. Didn't work out? Go back. Snapshots a profile moves past aren't thrown away either. They wait under **Recently removed** until you clear them, and you can bring one back as a new profile.
- **Split your playthrough.** Sided with the wrong faction? Go back to the snapshot from before the choice and keep the new session as its own profile. Both stories stay playable.
- **See what's happening.** Activity shows every job (a snapshot, a switch, freeing up space) with its progress and details, and you can stop most of them.
- **Keep the disk in check.** Files are only stored once, so a snapshot that changes just a few saves takes up very little space. **Free up space** shows you what it'll delete before it deletes anything, and you can move SavePoint's storage to another drive.
- **Make it yours.** Light and dark themes with five accent colors, a sidebar or a library layout, and cover art for Steam games (other games get a color tint from their name, and your own image always wins). It works fully from the keyboard, with screen readers, and with reduced motion, and it sends a notification when a long job finishes while you're in another window.

![History shows every snapshot as a card, each with Go back to this](docs/images/history.png)

New to all this? Press **F1** for a short illustrated guide, take the tour, or click a question button like "What's a snapshot?" next to anything new. You don't need to know anything about version control.

## Playing it safe

SavePoint only touches the folders you give it, and it's careful with them:

- It won't accept a whole drive or a system folder, and it asks before it takes over a folder.
- It never switches over new changes in the folders. You keep them, discard them, or take a snapshot first.
- Anything that can't be undone tells you so and asks a second time.
- When something goes wrong, it tells you what happened and what to do next, in plain words.

Two things to watch out for:

- **Online games with anti-cheat.** SavePoint never touches a game while it's running, but online games protected by anti-cheat check their own files. Changed or rolled-back files can get your account flagged, and those games can't be modded or rolled back anyway. Games you can play offline with anti-cheat turned off for mods are fine.
- **Cloud saves.** Turn off Steam Cloud or GOG Galaxy sync for any game SavePoint manages, or the two will keep overwriting each other.

Everything stays on your computer. The only network request SavePoint ever makes is the optional artwork lookup on Steam, and you can turn that off in Settings.

## Install

Grab a package from the latest release:

- **Windows:** run `SavePoint-win-Setup.exe` (it installs for your user and adds a Start menu shortcut), or unzip `SavePoint-win-Portable.zip` anywhere and run `SavePoint.exe`. The packages aren't code-signed yet, so SmartScreen will ask before the first run.
- **Linux:** run `chmod +x SavePoint.AppImage`, then run the AppImage.
- **macOS:** not packaged yet.

SavePoint never phones home for updates. To update, run a newer setup over the old one. Uninstalling removes the app but keeps your snapshots and settings. If you want those gone too, use **Settings > Remove all data** before you uninstall.

## Under the hood

SavePoint is a native C# app (.NET 10 and Avalonia) that runs on Windows, Linux, and macOS from one build. Snapshots are stored with git, the tool programmers use to track code, so they're compact and every file comes back byte for byte. SavePoint bundles its own pinned copy and never uses one installed on your machine, and you'll never see a git word in the app.

To build it yourself, you need the .NET 10 SDK (and PowerShell 7 on Windows):

```bash
dotnet build Savepoint.slnx -c Release
```

```bash
dotnet run --project src/Savepoint.App -c Release
```

The first build downloads the pinned git bundle and checks its SHA-256.

## Documentation

The guides and the API reference are published at [docs.tortal.tech/SavePoint](https://docs.tortal.tech/SavePoint/), built from `docs/` and the code on `main`.

- [User guide](docs/user-guide.md): every screen and action, for players.
- [Developer guide](docs/developer-guide.md): building, testing, gates, and packaging.
- [Architecture](docs/architecture.md) and [Decisions](docs/decisions.md): how it's built and why.
- [UI guide](docs/ui-guide.md): how to build a screen.
- [Testing on macOS and Linux](docs/testing-on-mac-and-linux.md): the gate and the manual checks on a Mac or a Linux desktop.
- [Dogfood checklist](docs/dogfood-checklist.md): the checks that need a person, real hardware, or a real game.
- [AGENTS.md](AGENTS.md): the working rules for every change.

Planned work is tracked in the issues and milestones.

## License

SavePoint is free software: you can redistribute it and modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version ([LICENSE](LICENSE), SPDX `GPL-3.0-or-later`). It comes with no warranty.

The bundled git is licensed under GPLv2 and runs as a separate program. Its license texts, the other third-party notices, and the source offer ship next to the app ([THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), `licenses/`).
