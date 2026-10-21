# SavePoint

SavePoint keeps a modded game playable exactly as you set it up, while the game itself keeps getting updates. It remembers the game's files and your saves together. You can switch between setups, go back to any earlier point, and pick up where you left off, and SavePoint never needs to know how a game stores its mods.

You keep your game where it is. SavePoint records each folder's contents in its own storage, and when you switch, it puts the right files back in the game's folders before the game starts.

**Status:** pre-release, in active development. This is v2, a native rewrite of the unreleased Electron v1 (see `docs/decisions.md`).

## What it does

- **Profiles.** Each profile is its own line of play, for example "Vanilla 1.5" and "Modded 1.6". Each keeps its own history of the game's folder and of your saves folder.
- **Save points.** Saving a point records what is on disk now: the game version and mods, the saves, or both. Points are stored once, so a save that only changes a few files costs little space.
- **Play from SavePoint.** Pick a profile or a point and press Play. SavePoint shows exactly what it will change, switches the folders, starts the game (a program or a Steam link), and follows it. When you finish, it asks what to do with the session: keep it as a new point, update the latest point, keep it as a new profile, or leave it for later.
- **Go back to any point.** History lists every point, as a list or as cards. Start the game from any of them. Afterwards you can continue that profile from there, or keep the progress as a new profile so nothing is lost.
- **Your storage, your call.** See how much space each game uses, optimize storage, move SavePoint's storage to another drive, or clear all of SavePoint's data. Clearing never touches your game's own files.
- **Careful by default:**
  - It refuses whole drives and system folders, and asks before it takes over a folder.
  - It never switches over unsaved changes: you keep them, discard them, or save them first.
  - Destructive actions need a second confirmation.
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

- **Windows:** run `SavePoint-win-Setup.exe`. It installs for your user only and adds a Start menu shortcut. Or unzip `SavePoint-win-Portable.zip` anywhere and run `SavePoint.exe`. The packages are not code-signed yet, so Windows SmartScreen asks before the first run.
- **Linux:** make `SavePoint.AppImage` executable and run it.
- **macOS:** not packaged yet.

SavePoint never checks for updates on its own. To update, run a newer setup over the old install.

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

- [Developer guide](docs/developer-guide.md): building, testing, gates, and the rules for changes.
- [Architecture](docs/architecture.md): the layers and what each one promises.
- [Decisions](docs/decisions.md): every departure from, and gap-fill of, the v1 spec, with reasons.
- [UI guide](docs/ui-guide.md): how to build a screen.
- [Roadmap](docs/roadmap.md) and [progress log](docs/progress-log.md): what is done and what is next.

## License

SavePoint's own license has not been chosen yet. The bundled git is licensed under GPLv2. A release will ship its license texts and source offer next to the app (D21).
