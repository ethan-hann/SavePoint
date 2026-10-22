# SavePoint - user guide

SavePoint keeps the setup that works. Point it at a game you have installed and modded. It remembers that exact version, its mods, and its saves, so you can come back to them later even after the game updates.

## The idea in three words

- **A game** in SavePoint is the game's install folder, and optionally the folder its saves live in.
- **A profile** is one way of playing it, for example "Vanilla" or "Modded 1.6, survival". Each profile remembers its own game version, mods, and saves.
- **A point** is a moment SavePoint saved for a profile. Each time you save a profile or finish playing, a new point appears in History, and you can go back to it.

SavePoint keeps its copies in its own storage folder. When you play a profile, it puts that profile's files back into the game's folders first, then starts the game.

## Getting started

On first start, SavePoint offers **Add your first game** or **Start from a template**. A template fills in the details of a known game; you can also open a game definition file someone exported. The first-run screen also says that SavePoint downloads cover art for Steam games from Steam; you can turn that off in Settings.

### Adding a game

1. **Name** the game.
2. **Where the game is installed**: choose the game's own folder, for example `C:\Games\SkyrimSE`. SavePoint refuses whole drives and system folders, because it replaces everything inside the folder you give it.
3. **Where the saves live** (optional): many games keep saves outside the install folder, for example under `Documents\My Games`. SavePoint suggests common locations.
4. **How to start the game**: run the game's program, or go **Through Steam** with a launch link such as `steam://run/489830`. The number is in the address of the game's Steam store page. Starting through Steam can let Steam update the game again right after SavePoint puts your version back, so running the program directly is safer.
5. Optional settings:
   - **Extra command-line options.**
   - **Knowing when you've stopped playing:** the name of the game's own process, for launchers that close themselves before the game opens.
   - **Files to ignore:** for example `**/*.log`, so logs and crash dumps never count as changes. A file on this list is left as it is when you switch profiles and is no longer saved, even where older points saved it, so keep the list to files that don't matter: a pattern that names your saves would stop profiles from switching them.
   - **Folders that hold mods:** these only change what History highlights as a mod change.
6. **Save changes**. SavePoint asks you to confirm each folder. It keeps copies of everything in them, and it leaves one small marker file in each folder to remember you chose it.

Before you start:

- **Kernel-level anti-cheat:** games protected by it are not supported.
- **Cloud saves:** turn them off for the game in its store (Steam Cloud, GOG Galaxy). Otherwise the store can sync older saves over the ones SavePoint puts back.

If a folder you add still holds history from a game you removed earlier, SavePoint offers **Reconnect** to bring that history back, or **Start fresh**.

### Artwork

Steam games show their cover art in the library and the sidebar.
- **Where SavePoint finds the art:** first in the Steam client's own cache on your computer, then, if **Look up game artwork online** is on in Settings > Appearance, from Steam's image servers.
- **Which game it is:** SavePoint reads the Steam app id from a `steam://run/<id>` launch link, from a `steam_appid.txt` file in the game folder, or from Steam's library files. You can also type it in **Steam app ID** in the editor's Artwork section; it's the number after `/app/` in the game's Steam store address.
- **Your own image:** **Choose image...** picks one (PNG, JPEG, or WebP, up to 25 MB). SavePoint keeps a copy, so moving the original file doesn't matter, and your image always wins. **Remove your image** goes back to the Steam art.
- **When nothing is found:** the game shows a color tint based on its name. **Look again** retries; otherwise SavePoint retries on its own after a week.

**Edit game** changes any of these later. **Export** writes the game's definition to a file you can share, and **Import** loads one. **Delete game** removes the game, its profiles, and all its saved history from SavePoint, after you type the game's name to confirm. The files in your game folder and saves folder are not touched.

## Profiles

On the Games screen, **New profile** walks you through three steps:

1. **Name it:** a name and optional notes.
2. **Check:** what will be captured from each folder, and how much new space it takes. SavePoint reuses files it already has, so later profiles usually add little.
3. **Save:** **Save profile** saves it. A large install takes a while the first time.

The profile table shows, for each profile:

- whether it is **Active now** or has **Unsaved changes**;
- how many saves it holds;
- its game version and mod files;
- when you last played it.

Each row has **Play**, plus **Edit** (name, notes, version), **Duplicate**, and **Delete**:

- **Duplicate** starts a new profile with the same game version and mods and a fresh save history.
- **Delete** warns you when the profile holds points no other profile has. Those points are lost for good. Their space is freed soon after, when SavePoint optimizes storage.

With a row selected from the keyboard, Enter starts Play.

## Playing

Press **Play** on a profile, or **Go back to this** on a point in History. SavePoint first shows exactly what it will change:

- **Game version and mods:** what it switches from and to, how much will be replaced, and roughly how long that takes.
- **Save to load:** the profile's saves, the most recent one first.
- **Unsaved changes from your last session.** When files changed since the profile was last saved, you choose what happens to them before anything is switched:
  - **Save them first**, into the profile they belong to.
  - **Keep as a new profile**, when they don't belong to any profile, so nothing is lost.
  - **Discard them**, permanently, after a second confirmation that names the folder and counts the files it removes and puts back.

**Switch and play** puts the files in place, shows how much it has written and roughly how long is left, and starts the game. While the game runs, SavePoint shows a running card. If SavePoint cannot tell when you stop (some launchers hide the game), press **I'm done playing**.

### When you finish

SavePoint asks what to do with the session:

- **Keep this as a new point in your history** (recommended). Your previous saves stay exactly as they are.
- **Update the latest point instead.** This replaces that point with what is on disk now, after a second confirmation, and cannot be undone.
- **Not now.** Nothing is lost: your saves stay on disk, and SavePoint asks again before it switches profiles.

If you started from an older point, the progress can't simply go on the end of the profile, so the choices are:

- **Keep this as a new profile** (recommended). The original profile stays exactly as it is.
- **Continue the profile from here**, offered when every folder started the session from that profile's own points. The newer points leave that profile. You can still recover them until the next storage optimization.

If files in the game's own folder changed while you played, as an update would change them, SavePoint tells you.

To skip this question altogether, turn off **Ask whether to save the session** in Settings. SavePoint then saves nothing after you play; your progress stays on disk, and SavePoint asks about it before the next switch.

The check before playing can be skipped too. When only the save would change, tick **Don't ask again when only the save changes** in that check, or turn on **Settings > Before you play > Skip the check before playing when only the save changes**. A change to the game version or mods always shows the check.

## History

**History** lists every point you can go back to, newest first, grouped into the last 7 days and earlier. Each point is one of three kinds:

- **Version, mods and save**
- **Version and mods**
- **Save only**

Switch between the **List** and **Cards** views, and filter by profile. The point your game folders hold now is marked **Loaded now** on its card, and **in use now** in the list.
- **Go back to this** on a card, or on a version in the list, starts the game from that whole point.
- **Load** next to a save in the list starts the game with that save, in the profile it belongs to.

Both go through the same check of what will change as Play.

## Settings

- **Where SavePoint keeps its data:** how much space your games use.
  - **Change** moves SavePoint's storage to another folder or drive. SavePoint copies everything, checks every file, switches over, and then removes its old copy.
  - **Optimize storage** reclaims the space of deleted profiles and old points no profile uses any more. It also runs automatically after you delete a profile or a game.
- **Appearance:**
  - theme: light, dark, or match the system;
  - accent color: blue, violet, green, amber, or crimson;
  - layout: **Sidebar** keeps every game one click away, **Library** puts cover art up front. You can also switch layout from the Games screen.
- **Before you play:** whether to skip the check before playing when only the save would change. A change to the game version or mods always shows the check.
- **When you finish playing:** whether to ask about saving the session.
- **Remove all SavePoint data:** removes SavePoint's saved history, settings, game list, artwork, and logs (except today's open log file), and takes its marker file out of each game folder. Your game folders and everything in them stay exactly as they are. You confirm by typing a word.
- **About:** the version, the third-party notices, and **Open the log folder**.

Default locations, until you move them:

| | Settings and game list | Storage |
| --- | --- | --- |
| Windows | `%APPDATA%\SavePoint` | `%LOCALAPPDATA%\SavePoint\Storage` |
| macOS | `~/Library/Application Support/SavePoint` | `~/Library/Application Support/SavePoint/Storage` |
| Linux | `~/.config/savepoint` | `~/.local/share/savepoint/storage` |

## When something goes wrong

Every failure says what happened and what you can do; **Show details** gives the technical text. Common cases:

- **A file is in use.** A switch or a save stopped because another program held a file, or a switch didn't start because SavePoint found the file in use first. Close that program, usually the game or its launcher, and try again. A retry picks up where it stopped.
- **SavePoint can't read a file.** A file in a game folder can't be opened, usually because it belongs to another account or is marked private, so SavePoint can't tell whether it changed. Closing programs doesn't help: make the file readable for your account, or remove it, then try again.
- **Half of that save worked.** One folder saved and the other didn't. Fix the reason shown and retry just that part. Nothing already saved is affected.
- **The last switch didn't finish.** The folder holds files from two points. Switch again to finish it, or discard the changes to bring the folder back to a saved point.
- **A folder moved or is gone.** SavePoint still has everything it saved. Point it at the new location to carry on.
- **That folder doesn't look like the one SavePoint saved.** When you point a game with history at a new folder, SavePoint compares the folder with what it saved. If fewer than half of the saved files are there, it shows both counts and asks you to confirm, so a slip like choosing the parent folder can't lead to a discard that wipes it.
- **SavePoint's storage isn't reachable.** If the folder holding SavePoint's storage can't be reached at startup, SavePoint says so instead of showing empty history, and offers three ways on:
  - **Try again**, after you connect the drive or network share.
  - **Find the folder**, when the folder moved or the drive has a new letter. SavePoint checks that the folder you choose holds its storage before it uses it. Games that folder has no history for start again with their next save.
  - **Start with an empty folder**, when the old storage is gone for good. You confirm first. Your games and profiles stay listed, but their saved history stays behind in the old folder, and SavePoint no longer uses it. Each profile starts its history again the next time you save it.

  If the storage folder can be reached again by the time you choose, SavePoint says so and changes nothing; use **Try again**. **Find the folder** refuses a folder that has a shortcut (a link) inside it, and names it, because SavePoint never goes through one. If SavePoint can't write to the folder it starts in, it says so, and you can try again once the drive has room or the folder isn't read-only. **Start with an empty folder** also refuses when SavePoint's default folder already holds saved history, and tells you to use that history through **Find the folder**, or to move that folder away to start over.
- **Part of that session wasn't saved.** After you play from a point that left one folder as it was, because that folder belongs to another profile's history, SavePoint saves the folder that is yours and says the other was left as it was. Its changes are still on disk. When no folder could be saved at all, the card says **That session wasn't saved**, and the folder left alone is listed as left as it was, never as saved.
- **"Update the latest point" fails on a file another program holds,** even one that didn't change, because SavePoint can't tell whether it changed without reading it. Close that program and try again.
- **Some old copies couldn't be removed.** After a storage move, SavePoint lists any old files it couldn't delete, for example because another program had them open, so you can delete them by hand.
- **An unreadable settings file at startup.** SavePoint leaves the file as it found it. For each one, it offers to restore the last good copy or start that file empty. The unreadable file is moved aside, never deleted.

Closing SavePoint while it removes all data or moves its storage asks first, whether you close the window or quit with Cmd+Q on macOS. Stopping those halfway would leave a mess.

### Reporting a problem

SavePoint keeps a log of what it did and of every error, one file per day, with the newest 14 kept. **Settings > About > Open the log folder** shows it. When reporting a problem, send the log from the day it happened. It contains folder paths and game names from your computer, and nothing is ever sent anywhere by SavePoint itself.

| | Log folder |
| --- | --- |
| Windows | `%LOCALAPPDATA%\SavePoint\Logs` |
| macOS | `~/Library/Logs/SavePoint` |
| Linux | `~/.local/state/savepoint/logs` |

If SavePoint ever runs into a problem it can't recover from, it shows a last window with the details and the log folder, then closes. It doesn't carry on in an unknown state that could touch your game folders.

## What SavePoint touches

- **The game's install folder and saves folder.** SavePoint reads them when it saves, and replaces their contents when it switches or discards. It adds one marker file to each.
- **Its own settings folder, storage folder, and log folder** (tables above).
- **Nothing else.** SavePoint uses its own copy of git and never your system's.
- **The network, only for artwork.** The one network request SavePoint makes is the artwork download from Steam. It carries the game's Steam app id in the address and SavePoint's name and version as the program making the request, and nothing that identifies you. **Settings > Appearance > Look up game artwork online** turns it off.
