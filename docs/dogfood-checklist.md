# Dogfood checklist

The automated suite covers the engine, the services, and every screen's view model, and drives headless journeys over the real backend with a stand-in game. These checks need a person, real hardware, or a real game. Copy the list into a comment on issue #6 for the build you are checking, tick each item there, and open an issue for anything that failed or felt wrong.

Build the app from `main` (`dotnet run --project src/Savepoint.App -c Release`), or install a package from the releases page.

## With a real game

- [ ] Add a game you actually play, with its real install and saves folders. The folder confirmation names both folders; Steam games pick up their cover art, and others show a name tint.
- [ ] Create a first profile. The size estimate is plausible, and the save finishes without freezing the window.
- [ ] Play through SavePoint, quit the game, keep the session as a new snapshot. History shows it. During the switch, the dialog shows how much has been copied and roughly how long is left.
- [ ] Change something (install a mod, or let the game update), save a second profile, and switch back and forth. The game starts in the right state each time.
- [ ] Go back to an older snapshot from History, play, then keep the session as a new profile. The original profile is unchanged.
- [ ] Continue a profile from an older snapshot: its newer snapshots show under History > Recently removed, and Bring back as a new profile restores one.
- [ ] Help: press F1, read How SavePoint works, open each What's this? button, and take the tour from Settings > Help. Then do the same with Narrator, and by keyboard only.
- [ ] Read every screen as someone who has never heard of version control would. Note any word or sentence that needs explaining.
- [ ] Activity: start a snapshot of a large profile, open Activity from the status indicator, watch Details, copy them, and cancel it.
- [ ] Close SavePoint while a snapshot runs: it asks first, and Minimize keeps the snapshot going and tells you when it is done.
- [ ] Turn off the store's cloud saves for that game first (Steam Cloud, GOG Galaxy), as the editor recommends.

## Accessibility

- [ ] Narrator (and NVDA if you have it): walk the Games screen, the launch dialog, the after-play dialog, and Settings. Buttons, rows, and dialog messages are read; progress and banners are announced.
- [ ] Keyboard only: add a game, create a profile, play, finish. Tab order follows what is drawn, focus is always visible, and Escape closes dialogs. Enter on a selected profile row starts Play. While the banner or the running card shows at the bottom of the Games screen, Tab still reaches it after the profile rows.
- [ ] Windows display scaling at 150% and 200%: every screen fits or scrolls, nothing is cut off, and dialogs keep their buttons visible. Check the Games screen while a profile is saving, and in the Library layout while the banner shows.
- [ ] Turn off **Animation effects** (Windows Settings > Accessibility > Visual effects): progress shows a still bar instead of a sweep.

## Failure handling

- [ ] Hold a save file open in another program during a switch: the error names the file; after closing it, "Try again" finishes.
- [ ] Unplug or rename the drive holding SavePoint's snapshot folder, then start SavePoint: it says it can't reach your snapshots and offers Try again, Find the folder, and Start with an empty folder.
- [ ] Settings > About > Open the log folder: today's log exists and records what you did.
- [ ] Start SavePoint twice: the second start brings the first window forward instead of opening another copy.
- [ ] A crash: there is no crash button on purpose; if SavePoint ever closes with "SavePoint ran into a problem", note what you were doing and keep the log.

## Packages

- [ ] Run `SavePoint-win-Setup.exe` (SmartScreen will warn, since it is unsigned). The Start menu shortcut works; Settings > About shows the release's version and opens the third-party notices.
- [ ] Install over a release from before 0.9.11 and start it once: the snapshots move from `%LOCALAPPDATA%\SavePoint` to `%LOCALAPPDATA%\SavePoint Data`, and every game's history is still there (D38).
- [ ] The portable zip runs from any folder.
- [ ] Uninstall from Windows Settings > Apps. The app goes, but SavePoint's data stays in `%APPDATA%\SavePoint` and `%LOCALAPPDATA%\SavePoint Data`. Reinstalling picks your games and history up again (D38).
- [ ] macOS: follow `testing-on-mac-and-linux.md` (issue #2).
- [ ] A Linux desktop or VM: follow `testing-on-mac-and-linux.md` (issue #3). The AppImage starts, notifications appear as SavePoint, a game started through a launcher is followed, and starting it twice brings the first window forward (the tests for that skip under WSL and in CI).
