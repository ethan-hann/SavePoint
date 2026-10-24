# SavePoint - user guide

SavePoint keeps the setup that works. Point it at a game you've installed and modded, and it takes snapshots of that exact version, its mods, and its saves. You can go back to any of them later, even after the game updates.

## The idea in four words

- **A game** in SavePoint is the game's install folder, plus the folder its saves live in if you want.
- **A profile** is one way of playing it, like "Vanilla" or "Modded 1.6, survival". Each profile has its own game version, mods, and saves.
- **A snapshot** is a copy SavePoint keeps of a profile's game version, mods, and saves as they were at one moment. SavePoint takes one when you create a profile and, if you want, every time you finish playing. History lists them, and you can go back to any of them.
- **Saves** are only the game's own save files, the ones the game writes when you save in the game.

SavePoint keeps its snapshots in a folder of its own, the snapshot folder. When you play a profile, it puts that profile's files back into the game's folders first, then starts the game.

## Help inside the app

- **How SavePoint works:** a short illustrated page on profiles, snapshots, playing, keeping a session, going back, Recently removed, Activity, freeing up space, and what can't be undone. Press **F1** anywhere or open **Settings > Help**. The first-run screen links to it too.
- **Question buttons**, such as **What's a snapshot?** and **What are new changes?**, sit next to the ideas that need explaining, like snapshots, new changes, and replacing a snapshot. They open a short explanation right there, and Escape closes it.
- **The tour:** after you create your first profile, a four-step tour points out the profile row, Play, History, and New profile. You can skip it at any step or press Escape. **Settings > Help > Show the tour again** plays it again.
- **At the moment of a choice:** the first time you play from an older snapshot, the first time you discard changes, and the first time you replace a snapshot, SavePoint explains the choice right then. It remembers which ones you've already seen.

## Getting started

The first time you open SavePoint, it offers **Add your first game** or **Start from a template**. A template fills in the details for a known game, and you can also open a game settings file someone else exported. The first-run screen also tells you that SavePoint downloads cover art for Steam games from Steam. You can turn that off in Settings.

### Adding a game

1. **Name** the game.
2. **Where the game is installed**: pick the game's own folder, for example `C:\Games\SkyrimSE`. SavePoint won't accept a whole drive or a system folder, because it makes the folder you give it match a profile, and that replaces what's inside.
3. **Where the saves live** (optional): lots of games keep saves outside the install folder, for example under `Documents\My Games`. SavePoint suggests the common spots.
4. **How to start the game**: run the game's program, or go **Through Steam** with a launch link like `steam://run/489830`. The number is in the address of the game's Steam store page. Starting through Steam can let Steam update the game again right after SavePoint puts your version back, so running the program directly is safer.
5. Optional settings:
   - **Extra command-line options.**
   - **Knowing when you've stopped playing:** the name of the game's own process, for launchers that close themselves before the game opens.
   - **Files to leave out:** for example `**/*.log`. SavePoint never copies or replaces a file that matches, so logs and crash dumps never count as changes, and a switch leaves them alone. Only list files that don't matter, though. A rule that matches your saves would stop profiles from switching them. Rarely, a profile needs a file in a spot where one of these left-out files sits inside a folder with the same name. When that happens, SavePoint stops before changing anything and tells you which one, so you can move it yourself.
   - **Folders that hold mods:** these only change what History counts as a mod change.
6. **Save changes**. SavePoint asks you to confirm each folder, because it takes snapshots of everything in it, and when you switch profiles it makes the folder match that profile. It adds a small file to each folder so it remembers you picked it.

Before you start:

- **Online games with anti-cheat:** SavePoint never touches a game while it's running, but online games protected by anti-cheat check their own files. Changed or rolled-back files can get your account flagged, and those games can't be modded or rolled back anyway. Games you can play offline with anti-cheat turned off for mods are fine.
- **Cloud saves:** turn them off for the game in its store (Steam Cloud, GOG Galaxy). If you don't, the store can sync older saves over the ones SavePoint puts back.

If a folder you add still has snapshots from a game you removed earlier, SavePoint offers **Reconnect** to bring back its profiles and snapshots, or **Start fresh**.

### Artwork

Steam games show their cover art in the library and the sidebar.

- **Where SavePoint finds the art:** first in the Steam client's own cache on your computer. Then, if **Look up game artwork online** is on in Settings > Appearance, from Steam's image servers.
- **Which game it is:** SavePoint reads the Steam app id from a `steam://run/<id>` launch link, from a `steam_appid.txt` file in the game folder, or from Steam's library files. You can also type it into **Steam app ID** in the editor's Artwork section. It's the number after `/app/` in the game's Steam store address.
- **Your own image:** **Choose image...** picks one (PNG, JPEG, or WebP, up to 25 MB). SavePoint keeps its own copy, so it doesn't matter if you move the original, and your image always wins. **Remove your image** goes back to the Steam art.
- **When nothing is found:** the game shows a color tint based on its name. **Look again** tries again. Otherwise SavePoint tries again on its own after a week.

**Edit game** changes any of these later. **Export** writes the game's settings to a file you can share, and **Import** loads one. **Delete game** removes the game, its profiles, and all its snapshots from SavePoint, after you type the game's name to confirm. The files in your game folder and saves folder aren't touched.

## Profiles

On the Games screen, **New profile** walks you through three steps:

1. **Name it:** a name and optional notes.
2. **Check:** what goes into the first snapshot from each folder, and how much new space it takes. SavePoint reuses files it already has, so later profiles usually add very little.
3. **Create:** **Create profile** takes the first snapshot. A big install takes a while the first time.

The profile table shows, for each profile:

- whether it's **Loaded now** (its files are in your game folders) or has **New changes** (files changed since its last snapshot);
- how many snapshots it has;
- its game version and mod files;
- when you last played it.

Each row has **Play**, plus a menu with **Edit** (name, notes, version), **Take a snapshot now**, **Duplicate**, and **Delete**:

- **Take a snapshot now** keeps what your game folders hold right now as a new snapshot of the profile that's loaded. It's also how a profile with no snapshot yet gets its first one.
- **Duplicate** starts a new profile with the same game version and mods, but no save snapshots of its own.
- **Delete** warns you when the profile has snapshots no other profile has. Usually deleting the profile deletes those for good. Snapshots that Recently removed still holds a record of, as in a profile you brought back from there, go back to Recently removed instead and stay until you next free up space. The warning says which it will be.

If you select a row with the keyboard, Enter starts Play. In the Sidebar layout, the footer under the table shows how much space this game's snapshots use and how big the drive is. **Free up space** there clears out the snapshots no profile uses anymore, and it asks first when it would delete snapshots from History's Recently removed. In the Library layout, **Free up space** is in the game's menu.

## Playing

Press **Play** on a profile, or **Go back to this** on a snapshot in History. Before anything happens, SavePoint shows you exactly what it's going to change:

- **Game version and mods:** what it switches from and to, how much gets replaced, how much gets removed, and roughly how long that takes.
- **Saves to load:** the save snapshots you can choose from, newest first. A profile you made from another one also offers the saves it started with.
- **New changes since the last snapshot.** If files changed since the profile's last snapshot, you pick what happens to them before anything gets switched:
  - **Take a snapshot first** keeps them in the profile they belong to (the dialog names it).
  - **Keep as a new profile** is for when they don't belong to any profile, so nothing is lost.
  - **Discard them** asks you to confirm a second time, naming the folder and counting the files it deletes and puts back. This one is permanent.

**Switch and play** puts the files in place, shows how much it's copied and roughly how long is left, and starts the game. While the game runs, SavePoint shows a running card. If SavePoint can't tell when you've stopped (some launchers hide the game), press **I'm done playing**.

### When you finish

SavePoint asks what to do with the session:

- **Keep this session as a new snapshot** (recommended). Your earlier snapshots stay exactly as they are.
- **Replace "<snapshot>" with this session.** That snapshot holds this session instead. Whatever it held before goes to History's **Recently removed** until the next time you free up space.
- **Not now.** Nothing is lost. Your saves stay in your game folders as new changes, and SavePoint asks again before you switch profiles.

If you played from an older snapshot, the session can't just go on the end of the profile, so your choices are:

- **Keep this session as a new profile** (recommended). The original profile stays exactly as it is, newer snapshots and all.
- **Continue "<profile>" from this session**, offered when every folder started the session from that profile's own snapshots. The session becomes the profile's latest snapshot, and its newer snapshots are taken off the profile. They go to History's **Recently removed** until the next time you free up space.

If files in the game's own folder changed while you played, the way an update would change them, SavePoint tells you.

If you'd rather not be asked at all, turn off **Ask whether to keep the session** in Settings. Then SavePoint keeps nothing after you play. Your progress stays in your game folders, and SavePoint asks about it before the next switch.

You can skip the check before playing too. When only your saves would change, tick **Skip this check when only your saves change** in that check, or turn on **Settings > Before you play > Skip the check before playing when only your saves change**. A change to the game version or mods always shows the check.

## History

**History** shows the snapshots you can go back to, newest first, grouped into the last 7 days and earlier. Every snapshot is one of three kinds:

- **Version, mods and saves**
- **Version and mods**
- **Saves only**

You can switch between the **List** and **Cards** views and filter by profile. The snapshot your game folders hold right now is marked **Loaded now**.

- **Go back to this** on a card, or on a version in the list, starts the game from that whole snapshot.
- **Go back** next to a save snapshot in the list starts the game with those saves, in the profile they belong to.

Both go through the same check of what will change that Play does.

**Recently removed** lists the snapshots that "Replace" or "Continue" took off a profile. Each one stays there until the next time you free up space. **Bring back as a new profile** starts a profile from it and leaves the profile it came from as it is.

## Activity

**Activity** shows what SavePoint is doing, and what it just did, for the selected game: taking snapshots, switching and starting the game, and freeing up space. **All games** also shows moving the snapshot folder and removing SavePoint's data.

- Each job shows which profile it was for, how far it got, and when it started and finished.
- **Details** lists the files and steps it went through, and you can copy that into a bug report.
- **Cancel** stops a job that can be stopped. If a job can't stop at that moment, it tells you.

Clicking the job status indicator opens Activity too, and in the Sidebar layout the game's **Activity** tab does the same.

## Settings

- **Where SavePoint keeps your snapshots:** how much space your games use.
  - **Change** moves the snapshot folder to another folder or drive. SavePoint copies everything, checks every file, switches over, and then removes its old copy.
  - **Free up space** clears out snapshots no profile uses anymore, like a deleted profile's, plus the ones in Recently removed. When it would delete some of those, it asks first and tells you how many. A smaller cleanup also runs on its own after you delete a profile, and that one leaves Recently removed alone. Once Free up space has started deleting, it can't be stopped, and quitting waits for it to finish.
- **Appearance:**
  - theme: light, dark, or match the system;
  - accent color: blue, violet, green, amber, or crimson;
  - layout: **Sidebar** keeps every game one click away, and **Library** puts the cover art up front. You can also switch layouts from the Games screen.
- **Before you play:** whether to skip the check before playing when only your saves would change. A change to the game version or mods always shows the check.
- **When you finish playing:** whether to ask about keeping the session.
- **Remove all data:** deletes every snapshot, SavePoint's settings, game list, artwork, and logs (except today's open log file), and takes its small file out of each game folder. After that, you can't go back to any earlier version, mods, or saves. Your game folders and everything in them stay exactly as they are. You confirm by typing a word.
- **About:** the version, the third-party notices, and **Open the log folder**.

Default locations, until you move them:

| | Settings and game list | Snapshot folder |
| --- | --- | --- |
| Windows | `%APPDATA%\SavePoint` | `%LOCALAPPDATA%\SavePoint\Storage` |
| macOS | `~/Library/Application Support/SavePoint` | `~/Library/Application Support/SavePoint/Storage` |
| Linux | `~/.config/savepoint` | `~/.local/share/savepoint/storage` |

## When something goes wrong

Every error tells you what happened and what you can do about it. **Show details** gives you the technical text. The common ones:

- **A file is in use.** A switch or a snapshot stopped because another program had a file open, or a switch didn't start because SavePoint found the file in use first. Close that program (usually the game, its launcher, or a mod manager) and try again. A retry picks up where it stopped.
- **SavePoint can't read a file.** A file in a game folder can't be opened, usually because it belongs to another account or is marked private, so SavePoint can't tell whether it changed. Closing programs won't help here. Make the file readable for your account, or remove it, then try again.
- **A file and a folder share a name.** A profile needs a file where your game folder has a folder with that name, holding files SavePoint must not touch (or the other way around). SavePoint stops before changing anything and tells you which one. Move it yourself, then try again.
- **Only part of the snapshot was taken.** One folder made it into the snapshot and the other didn't. Fix the reason shown and retry just that part. Nothing that's already kept is affected.
- **The last switch didn't finish.** The folder has a mix of files from two snapshots. Finish the switch to carry on, or discard the changes to put the folder back to its last snapshot.
- **A folder moved or is gone.** SavePoint still has every snapshot. Point it at the new location to carry on.
- **That folder may be the wrong one.** When you point a game that has snapshots at a new folder, SavePoint compares the folder with its snapshots. If fewer than half of the files are there, it shows both counts and asks you to confirm. That way a slip like picking the parent folder can't lead to a discard that wipes it.
- **SavePoint can't reach your snapshots.** If the snapshot folder can't be reached at startup, SavePoint tells you so instead of showing an empty History, and gives you three ways forward:
  - **Try again**, after you connect the drive or network share.
  - **Find the folder**, when the folder moved or the drive has a new letter. SavePoint checks that the folder you pick holds its snapshots before it uses it. Any game that folder has no snapshots for starts over with its next snapshot.
  - **Start with an empty folder**, when the old folder is gone for good. You confirm first. Your games and profiles stay listed, but their snapshots stay behind in the old folder, and SavePoint doesn't use them anymore. Each profile starts over with its next snapshot.

  If the folder can be reached again by the time you choose, SavePoint tells you and changes nothing, so use **Try again**. **Find the folder** won't take a folder that has a shortcut (a link) inside it, and it names the shortcut, because SavePoint never goes through one. If SavePoint can't write to the folder it starts in, it tells you, and you can try again once the drive has room or the folder isn't read-only. **Start with an empty folder** also won't go ahead when SavePoint's default folder already holds snapshots. Instead it tells you to use them through **Find the folder**, or to move that folder somewhere else to start over.
- **Part of that session isn't in the snapshot.** Say you play from a snapshot that left one folder as it was, because that folder belongs to another profile. SavePoint keeps the folder that's yours and tells you the other one wasn't included. Its changes are still in your game folder. When nothing could be kept at all, the card says **SavePoint couldn't take a snapshot of that session**.
- **"Replace" fails on a file another program has open,** even one that didn't change, because SavePoint can't tell whether it changed without reading it. Close that program and try again.
- **Some old copies couldn't be removed.** After moving the snapshot folder, SavePoint lists any old files it couldn't delete (for example, because another program had them open), so you can delete them by hand.
- **An unreadable settings file at startup.** SavePoint leaves the file the way it found it. For each one, it offers to restore the last good copy or start that file empty. The unreadable file gets moved aside, never deleted.

If you close SavePoint while it's removing all data or moving the snapshot folder, it asks first when you close the window. Quitting with Cmd+Q on macOS is meant to ask the same way, but it hasn't been checked on a Mac yet. Stopping either of those halfway would leave a mess.

### Reporting a problem

SavePoint keeps a log of what it did and every error, one file per day, and it keeps the newest 14. **Settings > About > Open the log folder** takes you there. When you report a problem, send the log from the day it happened. It has folder paths and game names from your computer in it. SavePoint itself never sends anything anywhere.

| | Log folder |
| --- | --- |
| Windows | `%LOCALAPPDATA%\SavePoint\Logs` |
| macOS | `~/Library/Logs/SavePoint` |
| Linux | `~/.local/state/savepoint/logs` |

If SavePoint ever hits a problem it can't recover from, it shows one last window with the details and the log folder, then closes. It won't keep going in an unknown state that could touch your game folders.

## What SavePoint touches

- **The game's install folder and saves folder.** SavePoint reads them when it takes a snapshot, and makes them match a profile when it switches or discards. It adds one small file to each.
- **Its own settings folder, snapshot folder, and log folder** (see the tables above).
- **Nothing else.** SavePoint uses its own built-in copy of the tool that keeps snapshots, never one installed on your computer.
- **The network, only for artwork.** The one network request SavePoint makes is the artwork download from Steam. It carries the game's Steam app id in the address and SavePoint's name and version as the program making the request, and nothing that identifies you. **Settings > Appearance > Look up game artwork online** turns it off.
