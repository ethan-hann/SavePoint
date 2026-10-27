# Dogfood checklist

These checks need a person, real hardware, or a real game. The automated suite covers the engine, the services, every screen's view model, and headless journeys over the real backend with a stand-in game. Copy this list into a comment on issue #6 for the build you test, and tick each item there. Open an issue for anything that fails or feels wrong.

Build from `main` (`dotnet run --project src/Savepoint.App -c Release`), or install a package from the releases page.

## With a real game

- [ ] Add a game you play, with its real install and saves folders. The confirmation names both folders. Steam games show cover art, and others show a name tint.
- [ ] Add a game that keeps its mods outside its install folder (7 Days to Die from its template). The confirmation names all three folders. Make a profile with different mods, switch between the two, and check the game loads each profile's mods. A mod manager that deploys by file link is refused on Windows with a card that says why.
- [ ] Create a first profile. The size estimate is plausible, and the window does not freeze during the save.
- [ ] Play through SavePoint, quit the game, and keep the session as a new snapshot. History lists it. During the switch, the dialog shows the amount copied and the time left.
- [ ] Change something (install a mod, or let the game update), save a second profile, and switch back and forth. The game starts in the right state each time.
- [ ] Go back to an older snapshot from History, play, and keep the session as a new profile. The original profile is unchanged.
- [ ] Continue a profile from an older snapshot. Its newer snapshots show under History > Recently removed, and Bring back as a new profile restores one.
- [ ] Help: press F1, read How SavePoint works, open each What's this? button, and take the tour from Settings > Help. Repeat with Narrator, and by keyboard only.
- [ ] Read every screen as someone who does not know version control. Note any word or sentence that needs explaining.
- [ ] Activity: start a snapshot of a large profile, open Activity from the status indicator, watch Details, copy them, and cancel the snapshot.
- [ ] Close SavePoint while a snapshot runs. It asks first, and Minimize keeps the snapshot going and tells you when it is done.
- [ ] Open the editor for a game with the store's cloud saves on, then off. The cloud note shows only while they are on, and names the store.

## Accessibility

- [ ] Narrator (and NVDA if you have it): walk the Games screen, the launch dialog, the after-play dialog, and Settings. Buttons, rows, and dialog messages are read, and progress and banners are announced.
- [ ] Keyboard only: add a game, create a profile, play, and finish. Tab order follows the drawn order, focus is always visible, and Escape closes dialogs. Enter on a selected profile row starts Play. While the banner or the running card shows at the bottom of the Games screen, Tab reaches it after the profile rows.
- [ ] Windows display scaling at 150% and 200%: every screen fits or scrolls, nothing is cut off, and dialogs keep their buttons visible. Test the Games screen while a profile saves, and the Library layout while the banner shows.
- [ ] Turn off **Animation effects** (Windows Settings > Accessibility > Visual effects). Progress shows a still bar instead of a sweep.

## Failure handling

- [ ] Hold a save file open in another program during a switch. The error names the file, and after you close the file, "Try again" completes the switch.
- [ ] Unplug or rename the drive that holds SavePoint's snapshot folder, then start SavePoint. It says it can't reach your snapshots and offers Try again, Find the folder, and Start with an empty folder.
- [ ] Settings > About > Open the log folder: today's log exists and records what you did.
- [ ] Start SavePoint twice. The second start brings the first window forward and opens no second copy.
- [ ] Crash: there is no crash button on purpose. If SavePoint closes with "SavePoint ran into a problem", note what you were doing and keep the log.

## Packages

- [ ] Run `SavePoint-win-Setup.exe` (SmartScreen warns because it is unsigned). The Start menu shortcut works. Settings > About shows the release's version and opens the third-party notices.
- [ ] Install over a release older than 0.9.11 and start it once. The snapshots move from `%LOCALAPPDATA%\SavePoint` to `%LOCALAPPDATA%\SavePoint Data`, and every game's history is still there (D38).
- [ ] The portable zip runs from any folder.
- [ ] Uninstall from Windows Settings > Apps. The app is removed, and the data stays in `%APPDATA%\SavePoint` and `%LOCALAPPDATA%\SavePoint Data`. Reinstalling restores your games and history (D38).
- [ ] macOS: follow `testing-on-mac-and-linux.md` (issue #2).
- [ ] Linux desktop or VM: follow `testing-on-mac-and-linux.md` (issue #3). The AppImage starts, notifications show as SavePoint, a game started through a launcher is followed, and a second start brings the first window forward.
