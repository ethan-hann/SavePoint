# SavePoint - user guide

SavePoint keeps the setup that works. Point it at a game you have installed and modded. It remembers that exact version, its mods, and its saves, so you can come back to them later even after the game updates.

## The idea in three words

- **A game** in SavePoint is the game's install folder, and optionally the folder its saves live in.
- **A profile** is one way of playing it, for example "Vanilla" or "Modded 1.6, survival". Each profile remembers its own game version, mods, and saves.
- **A point** is a moment SavePoint saved for a profile. Each time you save a profile or finish playing, a new point appears in History, and you can go back to it.

SavePoint keeps its copies in its own storage folder. When you play a profile, it puts that profile's files back into the game's folders first, then starts the game.

## Getting started

On first start, SavePoint offers **Add your first game** or **Start from a template**. A template fills in the details of a known game; you can also open a game definition file someone exported.

### Adding a game

1. **Name** the game.
2. **Where the game is installed**: choose the game's own folder, for example `C:\Games\SkyrimSE`. SavePoint refuses whole drives and system folders, because it replaces everything inside the folder you give it.
3. **Where the saves live** (optional): many games keep saves outside the install folder, for example under `Documents\My Games`. SavePoint suggests common locations.
4. **How to start the game**: run the game's program, or go **Through Steam** with a launch link such as `steam://run/489830`. The number is in the address of the game's Steam store page. Starting through Steam can let Steam update the game again right after SavePoint puts your version back, so running the program directly is safer.
5. Optional settings:
   - **Extra command-line options.**
   - **Knowing when you've stopped playing:** the name of the game's own process, for launchers that close themselves before the game opens.
   - **Files to ignore:** for example `**/*.log`, so logs and crash dumps never count as changes.
   - **Folders that hold mods:** these only change what History highlights as a mod change.
6. **Save changes**. SavePoint asks you to confirm each folder. It keeps copies of everything in them, and it leaves one small marker file in each folder to remember you chose it.

Before you start:

- **Kernel-level anti-cheat:** games protected by it are not supported.
- **Cloud saves:** turn them off for the game in its store (Steam Cloud, GOG Galaxy). Otherwise the store can sync older saves over the ones SavePoint puts back.

If a folder you add still holds history from a game you removed earlier, SavePoint offers **Reconnect** to bring that history back, or **Start fresh**.

**Edit game** changes any of these later. **Export** writes the game's definition to a file you can share, and **Import** loads one. **Delete game** removes the game, its profiles, and all its saved history from SavePoint, after you type the game's name to confirm. The files in your game folder and saves folder are not touched.

## Profiles

On the Games screen, **New profile** walks you through three steps:

1. **Check:** what will be captured from each folder, and how much new space it takes. SavePoint reuses files it already has, so later profiles usually add little.
2. **Name it:** a name and optional notes.
3. **Save:** SavePoint saves the profile. A large install takes a while the first time.

The profile table shows, for each profile:

- whether it is **Active now** or has **Unsaved changes**;
- how many saves it holds;
- its game version and mod files;
- when you last played it.

Each row has **Play**, plus **Edit** (name, notes, version), **Duplicate**, and **Delete**:

- **Duplicate** starts a new profile with the same game version and mods and a fresh save history.
- **Delete** warns you when the profile holds points no other profile has. Those points are lost for good. Their space is freed the next time storage is optimized.

## Playing

Press **Play** on a profile, or **Go back to this** on a point in History. SavePoint first shows exactly what it will change:

- **Game version and mods:** what it switches from and to, how much will be replaced, and roughly how long that takes.
- **Save to load:** the profile's saves, the most recent one first.
- **Unsaved changes from your last session.** When files changed since the profile was last saved, you choose what happens to them before anything is switched:
  - **Save them first**, into the profile they belong to.
  - **Keep as a new profile**, when they don't belong to any profile, so nothing is lost.
  - **Discard them**, permanently, after a second confirmation.

**Switch and play** puts the files in place, shows the progress, and starts the game. While the game runs, SavePoint shows a running card. If SavePoint cannot tell when you stop (some launchers hide the game), press **I'm done playing**.

### When you finish

SavePoint asks what to do with the session:

- **Keep this as a new point in your history** (recommended). Your previous saves stay exactly as they are.
- **Update the latest point instead.** This replaces that point with what is on disk now, after a second confirmation, and cannot be undone.
- **Not now.** Nothing is lost: your saves stay on disk, and SavePoint asks again before it switches profiles.

If you started from an older point, the progress can't simply go on the end of the profile, so the choices are:

- **Keep this as a new profile** (recommended). The original profile stays exactly as it is.
- **Continue the profile from here.** The newer points leave that profile. You can still recover them until the next storage optimization.

If files in the game's own folder changed while you played, as an update would change them, SavePoint tells you.

To skip the question when only the saves changed, use **Settings > When you finish playing**. A change to the game version or mods always asks.

## History

**History** lists every point you can go back to, newest first, grouped into this week and earlier. Each point is one of three kinds:

- **Version, mods and save**
- **Version and mods**
- **Save only**

Switch between the **List** and **Cards** views, and filter by profile. A point in use is marked **in use now**. In the cards view, **Go back to this** starts the game from that whole point. In the list view, **Load** next to a save starts the game with that save, in the profile it belongs to. Both go through the same check of what will change as Play.

## Settings

- **Where SavePoint keeps its data:** how much space your games use.
  - **Change** moves SavePoint's storage to another folder or drive. SavePoint copies everything, checks every file, switches over, and then removes its old copy.
  - **Optimize storage** reclaims the space of deleted profiles and old points no profile uses any more. It also runs automatically after you delete a profile or a game.
- **Appearance:**
  - theme: light, dark, or match the system;
  - accent color: blue, violet, green, amber, or crimson;
  - layout: **Sidebar** keeps every game one click away, **Library** puts cover art up front. You can also switch layout from the Games screen.
- **When you finish playing:** whether to ask about saving the session, and whether to skip the question when only the saves changed.
- **Remove all SavePoint data:** removes SavePoint's saved history, settings, and game list, and takes its marker file out of each game folder. Your game folders and everything in them stay exactly as they are. You confirm by typing a word.
- **About:** the version, and the third-party notices.

Default locations, until you move them:

| | Settings and game list | Storage |
| --- | --- | --- |
| Windows | `%APPDATA%\SavePoint` | `%LOCALAPPDATA%\SavePoint\Storage` |
| macOS | `~/Library/Application Support/SavePoint` | `~/Library/Application Support/SavePoint/Storage` |
| Linux | `~/.config/savepoint` | `~/.local/share/savepoint/storage` |

## When something goes wrong

Every failure says what happened and what you can do; **Show details** gives the technical text. Common cases:

- **A file is in use.** A switch or a save stopped because another program held a file. Close that program, usually the game or its launcher, and try again. A retry picks up where it stopped.
- **Half of that save worked.** One folder saved and the other didn't. Fix the reason shown and retry just that part. Nothing already saved is affected.
- **The last switch didn't finish.** The folder holds files from two points. Switch again to finish it, or discard the changes to bring the folder back to a saved point.
- **A folder moved or is gone.** SavePoint still has everything it saved. Point it at the new location to carry on.
- **An unreadable settings file at startup.** SavePoint leaves the file as it found it. For each one, it offers to restore the last good copy or start that file empty. The unreadable file is moved aside, never deleted.

## What SavePoint touches

- **The game's install folder and saves folder.** SavePoint reads them when it saves, and replaces their contents when it switches or discards. It adds one marker file to each.
- **Its own settings folder and storage folder** (table above).
- **Nothing else.** SavePoint uses its own copy of git and never your system's. It makes no network requests.
