# SavePoint v2 - decisions

Choices made for the rewrite. The original `../Savepoint/plan.md` remains the behavioral spec; this file records only where v2 departs from it or fills a gap it leaves open. Each entry: the decision, then why.

## D1. Rewrite in C# / .NET 10 with Avalonia UI

The original plan fixed Electron + TypeScript. The rewrite brief rules out TypeScript and asks for a native desktop stack. C# on .NET 10 with Avalonia UI:

- Native desktop app, no browser runtime, small memory footprint compared to Electron.
- Cross-platform UI toolkit (Windows, macOS, Linux), which keeps the plan's "a Linux/macOS adapter later is additive" promise. WPF and WinUI are Windows-only.
- .NET covers what the TypeScript app needed npm packages for: `Process` with `Kill(entireProcessTree: true)` (replaces the hand-rolled taskkill tree kill), process enumeration (replaces ps-list), `DriveInfo` (free space), `FileVersionInfo` (exe version), `System.Text.Json` (catalog), `ArgumentList` (no shell quoting of git arguments).
- Mature test tooling (xUnit) and headless UI testing (Avalonia.Headless), including rendering screens to PNG for visual review.

## D2. Keep the git-backed engine and bundled MinGit

Unchanged from the plan: one install repo and an optional external repo per game, Profile = branch, capture = commit, restore = checkout, split git-dir/work-tree. Git runs as a subprocess against a bundled MinGit, never a system git. The engine shells out directly (no LibGit2Sharp): libgit2 is weaker on large binary trees and packfiles, which is exactly this workload, and the plan already chose real git for that reason.

## D3. No IPC layer; a service facade instead

Electron needed IPC between renderer and main. A native app does not. The plan's IPC-boundary rules still apply, moved to a service facade the UI calls: long operations run as jobs with progress events, errors are translated to catalog message ids before they reach any view, and views never touch git or the file system directly.

## D4. UI copy in an ICU MessageFormat JSON catalog

The plan requires every user-visible string to come from a catalog, with plural and select support, and a test that bans git vocabulary. v2 keeps the v1 catalog format (`en.json`, ICU MessageFormat) rather than moving to .resx, so the finished v1 copy carries over and a translator works with the same format. Formatting goes through an ICU MessageFormat library; dates and numbers through `CultureInfo`.

## D5. Solution layout

- `src/Savepoint.Core` (net10.0): domain, engine, services, platform interfaces. No UI references. OS-specific tables and calls live under `Platform/Windows`, `Platform/Linux`, `Platform/MacOS` behind interfaces, selected at runtime (see D9).
- `src/Savepoint.App` (Avalonia): views, view models, theme, localization, OS integration (dialogs, notifications).
- `tests/Savepoint.Core.Tests`, `tests/Savepoint.App.Tests` (xUnit).
- `tools/FakeGame`: a stand-in game exe for end-to-end tests (the v1 repo had the same tool).
- `build/`: MinGit fetch with SHA-256 verification, packaging scripts.

## D6. Storefront auto-fetch stays out of scope

The plan marks it "Phase 6 (stretch, later)" and "deferred until v1's manual-capture model has proven itself". v2 ships the manual-capture model complete and does not build storefront fetching.

## D7. Engine-based ignore suggestions and compression knobs stay out

The v1 background-jobs proposal floated curated per-engine ignore lists (Idea B) and object-store compression knobs (Idea C). Both were dropped during v1 dogfooding: engine caches live in AppData, outside the tracked install, and git compression gains on binary assets are marginal. The win that shipped was capture speed through git config tuning, which v2 keeps.

## D9. v2 ships on Windows, Linux, and macOS

Confirmed by the owner on 2026-10-20: v2 is fully cross-platform. This replaces the plan's "Windows only for v1, seams for later". Consequences:

- Bundled git on every platform, still nothing for the user to install: GitHub Desktop's portable git builds (dugite-native: Windows, macOS x64/arm64, Linux x64/arm64), pinned by version and SHA-256. Windows can keep MinGit if dugite-native proves unsuitable there.
- Path-safety denylist tables and path tokens for all three OSes. The plan already sketches the macOS and Linux tiers; tokens map to the OS conventions (XDG dirs on Linux, `~/Library/Application Support` on macOS).
- Launch covers Windows `.exe`, Linux native binaries and scripts, macOS `.app` bundles, and storefront URIs through each OS's opener. Exe version reading uses a managed PE parser, so it also works for Windows games run through Proton on Linux.
- Notifications, single-instance, folder reveal, and OS theme detection sit behind platform interfaces with one implementation per OS.
- Copy that names the OS ("Match Windows", "a Windows folder") becomes OS-aware through ICU `select`.
- Packaging: an installer per OS (Windows installer + portable, macOS app bundle, Linux AppImage).
- Verification: Windows natively; Linux through the local WSL Ubuntu with a workspace-local .NET SDK and bundled git; macOS cannot run on this machine, so its code stays on shared POSIX paths, its tables are covered by data-driven tests, and it is flagged as unverified on real hardware.

## D10. Library and tool choices (verified 2026-10-20)

| Need | Choice | Why |
|---|---|---|
| UI toolkit | Avalonia 12.1.4 (`Avalonia`, `.Desktop`, `.Themes.Fluent`) | Current stable, active; 12 makes compiled bindings the default |
| MVVM | CommunityToolkit.Mvvm 8.4.2 | Source-generated observable properties and commands |
| Tests | xunit.v3 + xunit.runner.visualstudio + Microsoft.NET.Test.Sdk; Avalonia.Headless.XUnit 12.1.4 | xunit 2.x is deprecated; Avalonia's headless runner targets v3 |
| ICU messages | MessageFormat (jeffijoe) 8.0.0 | Plural, select, `=N`, `#`; one formatter instance per locale on the UI thread |
| Exe version | AsmResolver.PE.Win32Resources 6.0.1 | Managed PE resource parser, works on every OS (FileVersionInfo reads native PE version info only on Windows) |
| Packaging | Velopack 1.2.161 (`vpk`) | Installer + portable from one command on all three OSes; skip 1.2.158 (flagged broken) |
| Windows toasts | Windows Runtime `ToastNotificationManager` with our own AUMID, set on the Velopack shortcut | No third-party dependency; the toolkit packages are archived or deprecated |
| Bundled git | See D9; Windows fallback MinGit 2.56.0.2 (SHA-256 `da35e72aa21c005a5a0d298cfbae110bc1609a815730ea0dde84b01a1b3cd3be`) | Official build with published hash |

## D11. Port Material Color Utilities instead of depending on a C# port

The MD3 palette needs HCT, `SchemeTonalSpot`, and the surface-container roles. The only C# port with those roles (Bdziam.DynamicColor 0.0.11) has had no change since April 2025 and an unclear license. v2 ports the needed subset of Google's Apache-2.0 library (with attribution) and verifies it with golden vectors generated from the official `@material/material-color-utilities` 0.3.0, the exact version v1 pinned, for all five seeds in both modes. The token table in `../Savepoint/docs/ui/README.md` is a second check for the blue seed.

## D12. Static, subset fonts built from the official variable fonts

Avalonia does not render variable fonts. Roboto, Roboto Mono (OFL 1.1), and Material Symbols Rounded (Apache-2.0) ship only as variable fonts upstream. v2 instantiates static weights with fontTools and subsets Material Symbols to the icons the UI uses, at its default axes (FILL 0, wght 400, GRAD 0, opsz 24). The generator script lives in `build/fonts/`; the generated fonts and their licenses are committed. Icons are referenced by code point, not ligature.

## D13. Resolutions for v1 defects found during the inventory

The inventories (`docs/reference/v1-services.md` section 10, `v1-ui.md` section 5.4, `v1-engine.md` gotchas) list behavior v1 got wrong or left undecided. v2 resolves each on purpose:

1. **Consent marker and ignore rules stay out of the game folders' history.** v2 never writes a `.gitignore` into a game's folder. `ignorePaths` and the marker file name are written to `<gitDir>/info/exclude`, which lives in SavePoint's storage. The marker is therefore never committed, so checking out an older point can never rewrite it. Previews read the same exclude file and never write into the work tree.
2. **A corrupt catalog file is never overwritten.** Missing means empty; unreadable or invalid JSON raises `CatalogCorruptException`, the app shows a recovery screen naming the file, and no write happens until it is resolved. Every overwrite first copies the previous good file to `<name>.bak`.
3. **Catalog writes are serialized** through one async lock per catalog store.
4. **Sessions live for the process lifetime.** Settings changes and storage moves swap dependencies; they never rebuild the launch orchestrator or drop a running session.
5. **A running game blocks destructive operations on that game**: delete game, delete profile, storage move, and clear all data refuse with a specific message until the session ends.
6. **`versionOverride` is part of the Profile schema.**
7. **History sorts by `DateTimeOffset`**, never by string.
8. **Launch checks both folders for unsaved changes before switching either.**
9. **Profile delete validates both folders before deleting from either**, so a refused delete changes nothing.
10. **The 30-second grace for a launcher stub stays**, but the running card offers "I'm done playing" from the first second, so the wait never blocks the user.
11. **`exePath` must be relative to the install folder** and must not escape it (`..`). The editor's Browse converts a picked file to a relative path or refuses one outside the install folder.
12. **Process-name matching** is case-insensitive on Windows and macOS, ignores a trailing `.exe` difference, and on Linux also matches the executable's file name (the kernel truncates process names to 15 characters, which would otherwise break games run through Proton).
13. **Read-only git calls run with `GIT_OPTIONAL_LOCKS=0`**, so previews and row stats never take `index.lock` while a capture or switch runs.
14. **Duplicate and discard take the per-game lock.**
15. **Exe version reads are cached** by the blob id of the exe at that profile's tip.
16. **Free-space checks target the volume that is written**: the storage volume for captures and optimization, the game folder's volume for switches.
17. **Automatic optimization is preemptible.** It starts after a capture when a repo passes 5000 loose objects. A launch, capture, or delete on the same game cancels it (repacking is safe to interrupt) and proceeds; the next capture re-triggers it.
18. **Cancel reaches every cancellable job.** Captures (including post-play), switches, optimization, and storage moves all take the job's cancellation token. A session wait is not cancellable; "I'm done playing" ends it.
19. **Launch flow state lives above the views**, so changing screens, layouts, or the selected game never loses a running session or its post-play prompt.
20. **Partial results are judged per folder.** A job that succeeded for one folder and failed for the other is reported as partial everywhere (the "Half of that save worked" card), never as success.
21. **Every failure has visible copy.** The five message ids v1 could emit without a catalog entry get entries, and no action fails silently.
22. **Game notes survive editing.**
23. **Default storage root is a local, never-synced folder**: `%LOCALAPPDATA%\SavePoint\Storage` on Windows, `$XDG_DATA_HOME/savepoint/storage` on Linux, `~/Library/Application Support/SavePoint/Storage` on macOS. v1 used `Documents\SavePoint`, which OneDrive commonly syncs, a bad home for tens of GB of game data. First run shows the location and Settings can move it.
24. **Git never reads the user's global or system config.** Every git process runs with `GIT_CONFIG_NOSYSTEM=1`, `GIT_CONFIG_GLOBAL` pointed at an empty file, and a fixed identity, so a user's `commit.gpgsign`, hooks path, or aliases can never change what SavePoint does.

## D14. Playing from an older point, then saving

`plan.md` ("Save history within one Profile") requires two choices when the player continued from an older point and then captures: move the profile forward from that point (the newer points stop being part of the profile, recoverable until the next storage optimization) or fork a new profile from that point, with fork as the safer default. v1 never built this (it threw `detachedCaptureNotSupported`, later patched with keep-as-new in the launch dialog). v2 builds it:

- The post-play dialog, when the session started from an older point, offers: **Keep this as a new profile** (default, name field prefilled), **Continue "<profile>" from here** (moves the profile forward; second confirmation that names how many newer points leave the profile), and **Not now**.
- `SessionCompletion` variants: `CaptureNew(message)`, `UpdateExisting(message)` (amend), `ForkNewProfile(name, message)`, `MoveForward(message)`, `Skip`.
- Engine: fork = create a branch at the current (detached) commit, switch to it without touching files, commit; move forward = point the branch at the current commit (`reset --soft` semantics on the branch ref, never touching files), then commit. Both refuse unless the folder is on the commit the session started from or a descendant of it.

## D15. History points and "Go back to this"

A history point is one capture event. Each capture writes a `Savepoint-Capture: <guid>` trailer into the message of every commit it creates (one per folder), so points are grouped exactly, never by matching timestamps. The trailer is never shown.

- Point kinds (2j): **Version, mods and save** (both folders changed), **Version and mods** (install only), **Save only** (external only). The card's line ("Save only · 1.6.640 with 212 mod files") reads the install state current at that point.
- **Go back to this** opens the launch flow targeted at that point: each folder is restored to its commit for that point, or, for a folder the capture did not touch, to that profile's latest commit at or before it. A point that is not the profile's tip restores detached, and the next capture after playing follows D14 (fork by default).
- The launch dialog's save picker is the same mechanism restricted to save points of the target profile (plan step 2 and 2b).

## D16. Large incompressible files skip delta compression

Per the plan's Maintenance section, every repo gets `<gitDir>/info/attributes` (not a file in the game folder, consistent with D13.1) marking already-compressed formats `-delta`: archives (`zip 7z rar gz xz zst cab`), media (`mp4 webm mkv bik bk2 usm ogg mp3 flac`), and common game containers (`pak ucas utoc bsa ba2 archive vpk pck`). Text and small binary files keep delta compression.

## D17. Deleting a profile warns about history only it holds

The plan's profile list says Delete "warns if it's the only place holding some history". v2 computes, per folder, the points reachable from that profile and from no other profile (`rev-list <branch> --not <every other profile branch>`). The delete confirmation names how many points will be lost and that their space is freed by the next storage optimization; zero means a plain confirmation.

## D18. Startup checks and recovery

On launch, before any screen: verify the bundled git (`git --version`); failure shows a dedicated error screen naming the path and what to reinstall, never a silent failure. Stale `index.lock` files under the storage root are removed once, by the first startup check and before any git work, so every lock found then predates the current process (SavePoint is single-instance and owns every git process on its own repos; see D31); during a run, a lock is removed only under the game's lock with no SavePoint git process in flight for that repo. A corrupt catalog file shows the recovery screen from D13.2.

## D19. Onboarding and editor guidance from the plan's risk table

- First run and the game editor say plainly that games with kernel anti-cheat are not supported (the plan's explicit non-goal), without attempting detection.
- The game editor recommends turning off the storefront's cloud saves for a managed game (Steam Cloud, GOG Galaxy), next to the launch-method choice.
- Long paths: `core.longpaths=true` everywhere, and the engine's tests include a tree deeper than 260 characters on Windows.

## D20. Translator workflow

The v1 FormatJS extraction step becomes two tests plus a short guide: every key referenced from XAML or C# exists in `en.json`, `en.json` has no unreferenced keys, and every additional catalog (`<locale>.json`) has exactly the same keys with ICU arguments that match English. Adding a language is dropping a file next to `en.json`; the locale list in Settings is driven by the files present. (Refined by D39: catalogs are embedded at build time, so a language is added to the source tree and ships in a new build, and Settings shows no list until a second catalog exists.)

## D21. Licensing of the bundled git

Distributing git means shipping the GPLv2 text and a written source offer pointing at the exact upstream source tags (git-for-windows `v2.56.0.windows.2`, git `v2.53.0`, dugite-native `v2.53.0-4`). The bundle is pruned of components SavePoint never uses (Git Credential Manager and its UI assemblies, Git LFS, network helpers), which also shrinks the installer; whatever stays is listed in `THIRD-PARTY-NOTICES.md` with its license.

## D22. Colors come from SchemeTonalSpot; the mockup hexes are approximate

The UI README's token table was evidently drawn with the legacy `Scheme` API (its light primary `#00639D` and light onPrimaryContainer `#001D34` match that API exactly), while v1 shipped `SchemeTonalSpot` at contrast 0, as the README itself instructs ("derive them from the theme instead", "hardcoding these hexes would break the accent-preset feature"). v2 follows v1: every role comes from `SchemeTonalSpot`. Dark mode lands within a few units of the table; light primaries are slightly less saturated. Fidelity and Content variants were checked and rejected: they match the light primary better but turn primary containers into the saturated seed color, which departs further from the mockup. Roles M3 does not define (warning, success) come from fixed-hue tonal palettes at the same tones M3 uses for error (40/100/90/10 light, 80/20/30/90 dark): warning hue from the mockup's gold `#F5D488` (the first pass used amber `#f9a825`, which read orange), success hue from green `#2e7d32`.

## D23. Api contract details settled after the contract landed

- **History count**: the History header counts capture events (D15 points), not column rows. The mockup's "9 points kept" double-counts one point that appears in both columns; the seeded data says 8.
- **Cancelled reads**: a read whose token is cancelled returns a failure with `error.operation.cancelled`; view models discard results of requests they cancelled and never show that message.
- **New profile rows**: the catalog row is written when the profile's first capture finishes with at least one folder saved. A capture that saves nothing leaves no row and no branch (fixes v1 defect 9). A partial result keeps the row and shows the "Half of that save worked" card with a retry for the missing folder.
- **Settings save**: `ISettingsApi.SaveAsync` never changes `StorageRoot` or `LastOptimizedAt`; storage moves only through `RelocateStorageAsync` and the optimized date only through optimization jobs.

## D24. Public APIs carry XML documentation; project docs ship with v2

- Every public type and member in `Savepoint.Core` has an XML doc comment (`///`): a `<summary>` stating the contract (what it promises, failure ids it can return, threading of events), plus `<param>`, `<returns>`, `<remarks>` only where they add information beyond the name. CS1591 is an error for `Savepoint.Core`. `Savepoint.App` documents its public view-model and service surface the same way; generated and view code-behind are exempt.
- The one-line rule for comments applies to inline implementation comments only; API docs are the contract and are written to be read without opening the code.
- Already-merged Core code gets a documentation pass once the in-flight branches land, then the warning is switched on.
- Project documentation (README, user guide, developer guide covering architecture, building, testing, and adding a language) is a v2 deliverable in M7, written by the orchestrator, not delegated. An API reference is generated from the XML docs.

## D25. No Git LFS; large files handled by git's own store and geometric repacking

Git LFS is pruned from the bundle on purpose. LFS solves network transfer: it keeps large files out of clones by storing them on an LFS server. SavePoint has no remote; everything is local. Locally, LFS would add a second object store with its own cleanup (`git lfs prune`), run a clean/smudge filter on every tracked file, and introduce a failure mode where a broken or missing filter leaves pointer files in the game folder instead of the game's real files. Git's own store already keeps each distinct file content once across every profile.

What LFS would have avoided is real, though: a full `git repack -a -d` rewrites every object, so on a 60 GB install each optimization would copy 60 GB. v2 therefore:
- optimizes with geometric repacking (`git repack -d --geometric=2`, both automatic and manual), which packs new objects and merges only small packs, so the large packs from the first capture are written once and left alone;
- sets `core.bigFileThreshold=128m`, so files above it are streamed into the store without delta search (game binaries and archives rarely delta well) and without loading them whole into memory;
- keeps the D16 `-delta` list for compressed formats below that size.

## D26. OS integration without a Windows-specific target framework

WinRT toasts (`ToastNotificationManager`, chosen in D10) need the `net10.0-windows10.0.x` target framework, which would split every project's build by OS. v2 keeps one `net10.0` target and uses:
- **Windows notifications**: `Shell_NotifyIconW` info notifications through P/Invoke on the main window's handle. Windows 10 and 11 deliver these as toasts in the notification center under the app's name and icon. Supersedes the WinRT row in D10.
- **Linux notifications**: `org.freedesktop.Notifications.Notify` over D-Bus with `Tmds.DBus.Protocol`, which Avalonia's Linux backend already depends on; silently skipped when no notification service runs.
- **macOS notifications**: `osascript -e 'display notification ...'` (no app signing or entitlement needed).
- Notifications fire only while the window is unfocused or minimized, and only for capture, optimize, storage move, and clear-data jobs (the v1 proposal's rule), never for launches.
- **Single instance**: a named mutex per user plus a named pipe; a second launch asks the first to show and focus its window, then exits.
- **Reduced motion**: read from the OS (Windows `SPI_GETCLIENTAREAANIMATION`, macOS accessibility "reduce motion", GNOME `enable-animations`), feeding `ThemeService.ReduceMotion`.
- **Reveal in file manager**: Explorer `/select`, `open -R`, `xdg-open` on the folder.

## D27. The Linux gate retries failed tests once, and says which

The Linux gate runs under WSL on the workspace's `/mnt/f` drvfs (9p) mount, because the workspace rule keeps every file inside `Savepoint-v2`. That mount shows rename/visibility races ordinary Linux file systems do not (a file just renamed into place is briefly "not found"), so file-heavy tests fail at random there, a different one each run. The Linux gate therefore re-runs failed tests up to twice and prints every test that needed a retry; a test failing all three times fails the gate. The Windows gate never retries, so real races in SavePoint's own logic are still caught there. Product code does not treat "file not found" as transient, since on a real file system that would hide genuine losses.

## D28. What a capture stores, and how optimization reclaims space

Findings from the engine work that change behavior the plan relies on:

- **A game folder's own `.gitignore` files are not honored.** (For a file SavePoint's patterns name that a point already recorded, D41.2 decides: a switch and a discard leave it as it is on disk, and a save stops recording it; a discard with patterns is therefore no longer one hard reset.) SavePoint must restore exactly what was on disk, so only SavePoint's exclude list (`info/exclude`: the marker and the game's `ignorePaths`) decides what is skipped. Capture lists files itself (`ls-files -z -o -m -d --exclude-from=<gitDir>/info/exclude`, never `--exclude-standard`) and stages them with `add -f --pathspec-from-file=- --pathspec-file-nul`; status/unsaved-change detection and capture previews use the same listing; clean uses `clean -fdx` with SavePoint's patterns passed as `-e`, so a file a mod's `.gitignore` names is still saved, still detected as unsaved, and still cleaned on discard.
- **Nested version-control folders are refused, not half-saved.** A folder inside the game that holds its own `.git` (a mod cloned from GitHub) would otherwise be stored as an empty pointer whose files never come back, or fail the capture outright. Capture and preview detect any nested `.git` directory or file and refuse with `error.capture.nestedRepository`, naming the folder, before staging anything. The user moves the mod's hidden history folder out and captures again.
- **Two optimization modes** (refines D25). *Routine* (`repack -d --geometric=2`) runs automatically after captures and never rewrites the large packs. *Reclaim* expires reflogs, then `repack -a -d` and `prune --expire=now`, dropping everything no profile reaches: it runs when the user presses "Optimize storage" and automatically after a profile or game is deleted (preemptible like any optimization, D13.17). So D17's "freed by the next optimization" and D14's "recoverable until the next optimization" hold. Repos keep `core.logAllRefUpdates=false`; reflogs add nothing for SavePoint.
- **Profile branch names are always lowercase** (`CatalogIds`), so case-only collisions on case-insensitive file systems cannot happen.
- **Symbolic links inside game folders**: see D29 (the earlier wording here was wrong for Windows).

## D29. Engine safety rules from the engine review

The independent review (`.tools/reviews/git-engine.md`) reproduced three ways the engine could damage files. v2 adopts these rules, enforced in the engine itself (not only by callers):

1. **No folder links inside a tracked folder on Windows.** Git for Windows follows directory junctions and directory symlinks, so a switch or discard could delete or overwrite files outside the game folder. Capture, preview, switch, discard, and clean scan the work tree for directory reparse points (junctions, directory symlinks, mount points) and refuse with `error.capture.linkedFolder` naming the first one; nothing is staged or written. On Linux and macOS git stores symlinks as links and never follows them, so they are captured and restored as links (native games ship `.so` symlinks).
2. **A switch never runs over unsaved changes.** Non-forced checkout refuses a folder with unsaved changes (`DirtyWorkingTreeException`) instead of letting the verify/re-sync pass overwrite them; the launch flow already resolves unsaved changes first (D13.8), and this makes the guarantee hold for every caller.
3. **Clean and discard refuse a folder with no saved point** (`error.profile.noPoints`): with nothing captured, "discard" would mean deleting the whole game folder.
4. **Repo delete only deletes a SavePoint git directory**: the directory must sit inside the storage root (component-wise containment) and carry SavePoint's config stamp and git's own layout (`HEAD`, `objects/`), or the delete is refused without touching anything.

## D30. Game artwork from Steam, the name tint as fallback, the user's own image always wins

Owner request (2026-10-21): show a game's real artwork when it can be found, fall back to the existing name tint when it cannot, and always let the user set their own image. This amends `docs/ui/README.md` ("v1 has no art source ... Do not invent artwork"): publisher artwork served by Steam is not invented, the tint stays the fallback, and nothing is ever generated.

- **Precedence:** the user's own image, then Steam artwork, then the tint. A game with none of the first two looks exactly as it does today.
- **Source: Steam only, no SteamDB.** SteamDB asks not to be scraped and sits behind bot protection; its images come from Steam's CDN anyway. Other sources need an account key (SteamGridDB, IGDB) and are out of scope. GOG (`goggame-<id>.info` in the install folder) is a possible follow-up.
- **The Steam app id** is an optional `SteamAppId` (positive integer) on the game definition, shown and editable in the editor's Artwork section; templates may carry it. A stored id always wins. Without one, the artwork service detects an id at lookup time, in this order: a `steam://run/<id>` or `steam://rungameid/<id>` launch link; a `steam_appid.txt` holding only digits in the install folder; or an install folder under `<library>/steamapps/common/<dir>` whose sibling `<library>/steamapps/appmanifest_<id>.acf` names `<dir>` as its `installdir` (case-insensitive on Windows and macOS). Detection reads files only and never writes the catalog.
- **Two images per game, chosen by tile shape.** The wide card block uses `header.jpg` (460x215). The square tiles (rail 24, headers 34/40) use `library_600x900.jpg`, center-cropped. Both are drawn with uniform-to-fill and clipped to the tile's radius. The user's own image is one image used for every shape.
- **Where the images come from, in order:** first the Steam client's own local cache, which needs no network: `<Steam>/appcache/librarycache/<id>/` and any subfolder of it, or the older flat `<id>_header.jpg` / `<id>_library_600x900.jpg`. The Steam folder is `HKCU\Software\Valve\Steam\SteamPath` on Windows; `~/.steam/steam`, `~/.local/share/Steam`, and the Flatpak `~/.var/app/com.valvesoftware.Steam/.local/share/Steam` on Linux; `~/Library/Application Support/Steam` on macOS. Then, only when the `LookUpArtworkOnline` setting is on (the default), Steam's CDN at `https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/<id>/<file>`, falling back to `https://cdn.cloudflare.steamstatic.com/steam/apps/<id>/<file>`. Both were verified on 2026-10-21 for an older and a 2025 release. No store API call is made (no `appdetails`, no rate limit).
- **Network rules:** HTTPS only; GET only; the two hosts above and no redirect to any other host; a 10-second timeout; a 5 MB response cap; `image/jpeg` content type, and the bytes must decode as an image before the file is kept. At most two requests are in flight. No cookies, and the only identifying data sent is the app id in the URL. Lookups start after the library has rendered, never before, and never delay any screen. A miss (404 or no id) is remembered with its time and retried after 7 days, or right away when the user presses "Look again" or changes the app id. These are the app's only network requests; with the setting off it makes none.
- **Storage:** images live under SavePoint's configuration folder (`<config>/artwork/<gameId>/`), not the storage root, so a storage move does not touch them. Deleting a game removes its folder, and "Clear all data" removes them all. The user's own image is copied in on save, never referenced in place. It is accepted only if it decodes as PNG, JPEG, or WebP and is at most 25 MB; otherwise the editor shows `error.artwork.notAnImage` or `error.artwork.tooLarge`.
- **UI:** the tile shows the tint until the image is decoded (off the UI thread, at display size), so lists never wait or flicker. The tile stays decoration for assistive technology, since the game name is always beside it. The editor's Artwork section shows a preview, where the art came from ("From Steam", "Your image", or "No artwork found"), "Choose image...", "Remove your image" when one is set, "Look again", and the optional Steam app ID field with the detected id as its hint. Settings > Appearance adds "Look up game artwork online", with a note that artwork already on this computer is used either way.
- **API:** a separate `IArtworkApi` (get, set the user's image, clear it, look again, and a change event keyed by game id). `IGamesApi` and the game summaries stay unchanged, so the fake backend, the contract tests, and the launch work do not move. Tests use a fake Steam folder and a fake HTTP handler. No test, on either gate, ever reaches the network: the backend's default is online lookup off unless a handler is supplied, and only the app's bootstrap supplies the real one.
- **Settled while building it:**
  - A read never waits: it returns what is on disk and starts any lookup in the background.
  - Steam images, including ones from the local cache, are copied into SavePoint's folder, so a Steam library change cannot take them away.
  - Any 3xx answer counts as a failure, so no redirect is ever followed.
  - The tile switches to the wide image at a width of 1.5 times its height.
  - "Look again" is disabled while the app id field is being edited.
  - When saving the artwork fails after the game itself saved, the editor stays open on the saved game and shows the error there.

## D31. Fixes from the backend and launch review

The independent review (`.tools/reviews/backend-launch.md`, findings BL-001 to BL-033) found two ways the merged backend could lose history or files, and several ways it could mislead the player. v2 adopts these rules:

1. **"Update existing" rewrites only the point the player named (BL-001).** A folder whose newest commit carries the named point's capture id is amended. A folder that changed but whose newest commit belongs to another point gets a new commit carrying the same capture id, so the update stays one point and no other point is ever rewritten.
2. **Ignore patterns never depend on an earlier call (BL-002).** Status, discard, clean, capture, and preview take the game's current ignore patterns from its definition on every call. Saving a game also rewrites each folder's `info/exclude` under the game lock.
3. **A missing storage folder is unreachable, not empty (BL-004).** SavePoint creates the storage folder only on first run, when no root is saved. Otherwise a missing root raises `error.storage.unreachable` everywhere the snapshot is read. A move refuses a source with no game folders while the catalog lists games, and game delete refuses while the storage is unreachable. A move scan that cannot read an entry fails the move and names the entry (BL-003).
4. **Re-pointing a folder with history is checked (BL-005).** When an edit changes the path of a folder that has saved points, SavePoint compares the new folder with that folder's newest point: how many recorded files are present at the same relative paths. When fewer than half are, the editor warns with both counts and needs an explicit confirmation. The discard confirmation names the folder and counts the files it removes and the files it restores. Discard is not refused, since bringing back deleted files is a legitimate use.
5. **Discard never runs under an open game (BL-006).** It is refused while any session for the game is open (including Exited and Completing) or the game's process is running, checked again inside the game lock. The fake backend refuses it the same way, so screen tests run against the real rule (RC-011).
6. **An unfinished switch is its own folder state (BL-007).** `FolderLaunchState` reports it together with a `LaunchTarget` the App can start. "Switch again" starts that recorded target, which completes the switch. A half-switched folder is never offered "Keep as a new profile"; Discard is always offered.
7. **A partial after-play save is retried as the same choice (BL-008).** The orchestrator and the profiles service share one `CaptureRunner`. Retrying a partial completion reruns that completion's plan for the failed folders under the original capture id, so the session still yields one point.
8. **A folder a profile never saved is left alone (BL-012).** Launching a profile that has no points in one folder (a saves folder added after the profile existed) leaves that folder as it is; the after-play capture adds it. No empty branches are seeded.
9. **One function picks the commit that stands for a point in a folder (BL-024),** shared by History and the launch resolver. A folder with no commit at or before the point is left as it is. Points are ordered by a millisecond capture time recorded in the trailer, falling back to commit order, so two captures within one second cannot be confused.
10. **A storage move deletes the old copies, and reports what it could not remove.** After the copy is verified and SavePoint has switched to the new root, the old git directories are deleted (an owner call; v1 left them). `StorageRelocationResult` lists the old copies left behind; the job ends Partial when the list is not empty and the App shows it. Removal ignores cancellation once the switch is done. A move writes an in-progress marker in its destination first; a later move to a folder carrying that marker is refused with copy saying an earlier move did not finish (BL-019).
11. **Closing during clear-all or a storage move asks first (BL-019).** On exit SavePoint cancels cancellable jobs and waits briefly for them.
12. **Consent-marker game ids are validated (BL-013).** A marker whose `gameId` fails the catalog's id rule counts as corrupt, and the storage paths refuse rooted ids, separators, and dot segments, so no path from a marker can leave the storage root (on Windows, a UNC id would otherwise send the user's network login hash to a remote server).
13. **Errors keep their meaning (BL-026).** A git operation failure that is not a file in use gets its own message id and copy; a duplicate game and "no saved history to reconnect" get typed errors.

The remaining findings (BL-009, BL-010, BL-011, BL-014 to BL-023, BL-025, BL-027, BL-028, BL-030 to BL-032) are fixed as the review recommends. D18's stale-lock rule is restated to match the code: the startup sweep removes every lock once, before any git work, since each one it finds predates the process (BL-029). `UnavailableLaunchApi` and its shipped copy move to the test project, since every shipped build has the orchestrator (YAGNI-001).

## D32. Builds send no telemetry

Avalonia's packages bring in `Avalonia.BuildServices`, whose `AvaloniaStats` task sends anonymous build telemetry (machine id, hashed project and machine names, OS, tooling) from every compile, unless `AVALONIA_TELEMETRY_OPTOUT=1` is set. SavePoint is a local-first app, and its builds run on the owner's machine, often by the hundred during development. `Directory.Build.targets` therefore defines an empty `AvaloniaStats` target, which replaces the package's, so no build of SavePoint sends anything whatever the environment holds. The shipped app was never affected; the package says its telemetry is build-time only. The .NET CLI's own telemetry is turned off by `DOTNET_CLI_TELEMETRY_OPTOUT=1`, which the gate scripts and the test runner script set.

## D33. Packaging with Velopack, no automatic update checks

The roadmap asks for an installer and a portable build per OS. v2 packages with Velopack 1.2.161, the newest stable release on 2026-10-21. The `Velopack` package goes in the App, and `VelopackApp.Build().Run()` is the first call in `Main`. The `vpk` CLI is pinned as a local tool in `.config/dotnet-tools.json`, never installed globally.

- **Windows (`win-x64`):** a self-contained `dotnet publish`, then `vpk pack`, which gives `SavePoint-Setup.exe` (per-user install, Start menu shortcut, uninstall entry) and `SavePoint-Portable.zip`.
- **Linux (`linux-x64`):** the same steps on Linux (under WSL here), giving an `.AppImage`.
- **macOS:** `vpk pack` must run on macOS, and the result must be signed and notarized before anyone else can run it. Neither can be done here, so macOS packages are documented but not produced (D9).
- **Every package holds** the app, the pruned git bundle in `git/` (copied by `build/Git.targets` on publish), `THIRD-PARTY-NOTICES.md`, and `licenses/`. Each release also publishes the source archives of the git bundles it ships (D21).
- **No automatic update checks.** The app makes no network request the user did not ask for (D30's artwork lookup is the only one, behind its setting), and no release feed exists yet. Velopack contacts a feed only when the app calls `UpdateManager`, and v2 never does. A newer release installs over an older one by running its setup. An opt-in "check for updates" belongs to a later decision once a feed exists.
- **Versions:** one `Version` property in `Directory.Build.props` (semver, pre-release until the owner calls it 1.0) feeds the assembly versions, the About section, and `vpk pack --packVersion`.
- **Settled while building it:**
  - **Version:** `0.9.0`.
  - **Startup order:** `VelopackApp.Build().Run()` is literally the first statement of `Main`, because vpk checks for it there and warns about a wrapper. A test reads `Main`'s IL to pin the order.
  - **vpk flags:** `--shortcuts StartMenuRoot` (no desktop shortcut), `--skip-updates`, and an `--exclude` that drops `.pdb` files and the two XML doc files.
  - **Outputs:**
    - Windows: `SavePoint-win-Setup.exe`, `SavePoint-win-Portable.zip`, `SavePointApp-<version>-full.nupkg` (vpk names it after the pack id, D38), and vpk's release index files.
    - Linux: `SavePoint.AppImage` and `SavePointApp-<version>-linux-full.nupkg`.
  - **From the final review (D41):** the commit goes into the informational version (`0.9.0+<commit>`), a tree with uncommitted changes is refused unless a trial package is asked for, the source archives' hashes are pinned in `build/git-manifest.json` (empty until the first `-Sources` run; the check runs before anything is published, and a download is kept only when it matches its pin, D42), and each package's contents are listed in `artifacts/release/contents-<rid>/` and checked for the git bundle, the notices, and the license texts.
  - **Not yet covered:** the packages are unsigned, so SmartScreen warns on the setup. Releases still need to publish the MSYS2 and MINGW package sources that D21 also promises.

## D34. A local diagnostic log and a crash handler

v1's plan keeps each error's English message as dev-facing detail "for logs and a Show details toggle", and nothing in v2 writes a log yet. A failure the user reports, or a crash, would leave no trace.

- **Core logs through its own small interface,** `IDiagnosticLog` (information, warning, error, each with an optional exception). The backend takes it in `BackendOptions`, and the default writes nothing, so tests stay quiet. Core gains no logging dependency and no static state.
- **What is logged:**
  - start-up: app version, OS, the git bundle's path and version, and the result of each startup check;
  - every Api failure, at the facade: the message id, the English message, and the exception type and stack;
  - every job: start, end, status, and duration;
  - launch session changes;
  - each step of a storage move or a clear-all;
  - failed git commands: the arguments, the exit code, and the end of stderr.
  Paths appear as they are; the log never leaves the machine.
- **The App writes the files with Serilog 4.4.0 and Serilog.Sinks.File 7.0.0** (proven libraries; the rolling, size limits, and safe writes are their job). The logger is created and passed in, never the static `Log`. Files are `savepoint-<date>.log` in the platform's logs folder (`AppDirectories.LogsDir`), rolled daily and at 10 MB, with the newest 14 kept. Errors are flushed at once.
- **Unhandled exceptions** from the UI thread, the thread pool, or unobserved tasks are logged. An unobserved task exception is logged and observed. A UI-thread or domain-level crash shows a last window ("SavePoint ran into a problem and has to close") with Show details and the log folder, then exits: continuing in an unknown state could touch game folders wrongly.
- **Settings > About gets "Open the log folder".** "Clear all data" also empties the log folder, except the file in use.
- **Settled while building it:**
  - `IDiagnosticLog.CurrentFile` names the open file, so clear-all can spare it: Windows refuses to delete it, and Linux or macOS would unlink the live file. The backend wraps the log so a log that throws can never break the no-exception rule of the Api.
  - Api failures are logged once, in `ApiGuard`, with the member's name: a `SavepointException` as a warning, anything else as an error. A caller's own cancellation is not logged, because screens cancel reads on every navigation.
  - Failed git commands are warnings, since probes allow some exit codes. Each session phase logs once, not on every progress tick.
  - The Serilog sink is unbuffered, so every line reaches the disk at once.
  - On a crash, a second crash while the last window is up is only logged, and a crash with no responsive UI thread gives up on the window after 5 seconds. The exit code is 1.
  - The startup screen for a backend that failed to start names the log folder and opens it.
  - From the final review (D41): the start-up line names the commit the package was built from; a failed artwork lookup is a warning; a discard, a switch, a save that left a folder alone or skipped it, a profile delete, and a game delete each log what they did, with the paths, the bytes, and the branch tips removed; an exception thrown by an event subscriber is logged; on a crash, running jobs are asked to stop before the last window shows.

## D35. The "only the save changes" switch belongs to the check before playing

v1's visual spec gives one setting two homes. The launch dialog's "Don't ask again when only the save changes" box (2b) writes it, which matches the code: the setting skips the pre-launch check when only the save would change. Settings (2k), however, lists it under "When you finish playing" as "Skip the confirmation when only the save changes", which reads as the after-play question (review finding UX-012).

Behavior stays as the launch dialog defines it. Settings moves the switch into its own "Before you play" group, labeled "Skip the check before playing when only the save changes", with the caption "A change to the game version or mods always shows the check." "When you finish playing" keeps only "Ask whether to save the session".

## D36. Calls on the UX and accessibility review's open questions

The review (`.tools/reviews/ux-a11y.md`) left eight questions open. They are settled as follows:

- **OS scaling (OQ1).** SavePoint works at 150% on 1366x768 and at 200% on 1920x1080, which leaves about 960x490 logical pixels once the taskbar is counted. The window's minimum size shrinks to fit that, and every screen scrolls vertically instead of clipping. Headless renders at those sizes prove it.
- **Slow scans (OQ2).** Play shows "Checking your files..." at once, before the preview returns, however long the scan takes. "Save them first" shows progress too. The diagnostic log (D34) times the scans for later tuning.
- **Screen readers (OQ3).** Live regions raise the UI Automation live-region event when their text changes, and banners and error cards are announced when they appear; tests assert the events. A manual pass with Narrator and NVDA is listed for the owner's dogfood run, since it cannot be automated here.
- **Focus after a refresh (OQ4) and the History filter's arrow keys (OQ8):** a headless test settles each one, and any failure is fixed.
- **Artwork disclosure (OQ5).** The lookup stays on by default (D30). The first-run screen says in one line that SavePoint downloads cover art for Steam games from Steam and can be turned off in Settings, so the one network request is disclosed before it happens.
- **Re-pointing a folder (OQ6).** It belongs to D31 item 4 and is built with the review fixes.
- **The "only the save changes" switch (OQ7):** see D35.

## D37. Fixes from the recheck of the review fixes

The recheck (`.tools/reviews/backend-launch-recheck.md`) found the D31 work complete: 29 fixed, 1 fixed differently, 3 partly fixed, 0 open. It also found four new warnings (RC-001 to RC-004) and eleven suggestions. v2 adopts the recheck's recommended fixes, with these owner-level calls:

1. **Storage that is gone for good (RC-001; refined by D41.4 to D41.6 and D42.1).** Unreachable storage is a startup finding, like an unavailable git, with three choices:
   - "Try again";
   - "Find the folder": re-point the saved root at a folder that holds SavePoint's stamped git directories for the catalog's games, as a settings change with no copy;
   - "Start with an empty folder", after a confirmation that names the old path.
2. **A folder a profile never saved (RC-002, refines D31 item 8).** It is still left as it is, but it is read like every other folder, so another profile's unsaved changes there block the launch with their owner named. The profile's first save of that folder is a new history of its own (a root commit), never a fork of another profile's branch.
3. **Folders "Go back to this" leaves alone (RC-003).**
   - Each one's current commit and branch at launch are recorded as its start, so fork and move-forward check it like every other folder.
   - "Continue from here" is offered only when that start lies on the profile's own branch.
   - For "Keep as a new point" and "Update existing", whether such a folder is saved or skipped is decided before the save, never discovered as a half-failure.
4. **Stray folders in the storage root (RC-004 and the scan suggestion).** A folder whose name is not a game id, such as "game - Copy", never fails a move after the switch. The move fails only on folders that could be SavePoint's.
5. **Files held open (the suggestions on the racy fix).** The launch preview names a held file and offers "Try again" instead of Keep and Discard. A folder whose only changes are held files counts as clean for a switch; a save treats it as "cannot tell" (D41.1). Only a sharing or lock violation counts as "in use"; access denied gets its own message, which names the file.

The partly fixed BL-019, BL-026, and BL-032 are completed as the recheck describes. On macOS, an application-level shutdown (Cmd+Q) also asks before quitting during clear-all or a storage move.

## D38. The installer's folder is never SavePoint's data folder

Velopack's Windows setup installs into `%LOCALAPPDATA%\<pack id>`, and on install and uninstall it replaces or removes that entire folder. D33 used the pack id `SavePoint`, so the setup owned `%LOCALAPPDATA%\SavePoint`. That is also where SavePoint keeps its default storage (every game's history) and its logs. Uninstalling, or installing over a copy that had already run portably, would have deleted all of it. Found while writing the dogfood checklist, before any package was installed.

- **The pack id is `SavePointApp`.** It lives in one place, `<SavepointPackId>` in `Directory.Build.props`, which both package scripts read. The setup therefore owns `%LOCALAPPDATA%\SavePointApp`, and SavePoint's data stays in `%APPDATA%\SavePoint` and `%LOCALAPPDATA%\SavePoint`.
- **Files people download keep the app's name.** The scripts rename vpk's outputs back to `SavePoint-win-Setup.exe`, `SavePoint-win-Portable.zip`, and `SavePoint.AppImage`.
- **A test pins the rule.** `PackageLocationTests` checks that no folder SavePoint keeps data in lies inside `%LOCALAPPDATA%\<pack id>`, and that both scripts take the pack id from the build properties.
- **Uninstalling keeps the user's data.** To remove that too, use "Remove all SavePoint data" before uninstalling.
- **A storage move refuses a destination inside SavePoint's own install folder,** since the next update or uninstall would delete it.

## D39. Calls on the final gap analysis

The final gap analysis (`.tools/reviews/gap-final.md`, GAP-001 to GAP-027) is settled as follows:

- **Languages (GAP-010, GAP-011; refines D20).** v2 ships English only. Catalogs are embedded at build time, so adding a language means adding `Localization/Assets/Lang/<code>/*.json` and rebuilding, not dropping in a file. Settings shows no language list until a second catalog exists. The saved locale stays `en`, and its validation message says only that the language is not available. The developer guide gets an "Adding a language" section.
- **API reference (GAP-013; refines D24).** No generated API reference site. SavePoint is an application with no SDK, and the XML docs already reach every IDE. D24's compiler enforcement stays for Core. The App's public surface exists for XAML binding, so CS1591 stays off there, and the App documents its non-obvious contracts by hand (GAP-027).
- **Profile rows (GAP-016).** A row stays a tab stop, because it carries the row's spoken summary, and Enter on a focused row starts Play. The table's semantics stay as they are.
- **Built for the polish pass:**
  - real byte progress during a switch (GAP-009);
  - the single-instance lock-file fallback, with the app icon on the "already running" window (GAP-014);
  - the unreachable stand-ins removed from the shipped app: `startup.unavailable.*`, `StartupPhase.Unavailable`, and the placeholder screen with its copy (GAP-017);
  - path-safety and overlap errors that carry the folder's role (GAP-015);
  - a copy pass (GAP-018, GAP-019): American spelling, one dash style (an em dash for asides, never a spaced hyphen), and helper lines that end with a period;
  - re-rendering 2a-2l and 3a-3c, reviewed against the mockups (GAP-007).
- **Left to the owner (GAP-004, GAP-006, GAP-008):** the manual dogfood run, code signing, the MSYS2 and MINGW source archives, and SavePoint's own license.
- **The final code review (GAP-005)** runs after the polish pass, over everything since the backend review.

## D40. Mockup differences left after the final renders

The final renders (2a-2l, 3a-3c) were compared with the v1 screenshots after the polish pass. Differences already covered by a decision (D19, D23, D30, D36, the accessibility review) stand. These had none, and are settled as follows:

- **History groups say "Last 7 days", not "This week" (2j).** "This week" leaves open whether the week starts on Sunday, Monday, or seven days ago, and the grouping is the last seven days.
- **Quiet text uses the `SubtleText` role at tone 40 (light) and 70 (dark), not `outline` (the token table).** `outline` fails WCAG AA contrast for body-size text on the surface colors; the role keeps the mockup's lighter look at a contrast that passes.
- **The saves folder preview shows the full resolved path (2f).** A shortened path can hide which of two similar folders was chosen, and the editor is where that mistake would be caught.
- **The "Try something like..." suggestion on a refused folder sits in the error's paragraph (2l),** not on a colored line of its own under the path, so it reads as part of the error it belongs to.
- **The launch banner (2a) moves to the bottom of the pane, as the mockup draws it,** pinned above the storage footer rather than scrolling under the table. The checking, saving, and running cards go there too. A failure or partial-result card stays under the table and scrolls into view: it can be about 200 px tall, and pinned it would leave the table about 17 px of room at the smallest supported window.

## D41. Calls on the final code review

The final review (`.tools/reviews/final.md`, FR-001 to FR-032, YAGNI-001 to YAGNI-006) found no CRIT, six WARN, and confirmed the engine review's fixes. It is settled as follows; everything not named here is fixed as the review recommends, or skipped by the fix agent with a one-line reason.

1. **A held file is "cannot tell" when saving (FR-001, refines D37.5).** For a switch, a folder whose only changes are files SavePoint cannot open still counts as clean, and the launch names the file and offers "Try again". When saving, such a folder is never skipped as clean: the save runs, fails with the held-file error that names the file, and the retry that already exists applies. The after-play dialog counts held files. A consequence: an unchanged file that another program holds also fails "Update existing" until it is let go, since SavePoint cannot tell it is unchanged.
2. **Files the ignore list names (FR-002, settles the engine review's WARN-004; amended by D43.1).** A switch leaves every file the ignore list names exactly as it is on disk, whether or not some point recorded it, and never refuses because of one, except where a file and a folder would have to share a path (D43.1). A save stops recording such a file, so later points do not hold it; the earlier points keep it. A discard leaves it as well. A switch that is stopped part way must still leave the file as it was. A path that is a file at one point and a folder at the other is classified whole (each group of paths gets its own index), so a switch between them never removes a file the patterns name.
3. **"Try again" after a failed check re-enters the check for the target the player chose (FR-003).** Nothing from an earlier start carries over.
4. **A storage root change gives every listed game its repositories (FR-004).** After "Start with an empty folder", and for each game a "Find the folder" target lacks, SavePoint creates the game's empty repositories at once. Games and profiles stay listed; each profile starts its history again with its next save, and the confirmation says so.
5. **Git never runs a program a repository names (FR-005).** Every git process starts with hooks pointed at an empty folder SavePoint owns and `core.fsmonitor` off, set through the environment so a repository's own config cannot override them, and SavePoint keeps only the keys `git init` writes plus its own in a repository's config. A folder adopted through "Find the folder" has its configs rewritten and its hooks folders emptied before any other git call.
6. **"Find the folder" runs the checks a storage move runs on its destination (FR-006):** the installer-owned folder (D38), an unfinished move's marker, and overlap with game folders.
7. **A left folder never forks another profile's history (FR-009).** D37.2 wins over the fork that "Keep as a new profile" would make: that folder's first point in the new profile has nothing behind it.
8. **The fallback after-play choices for an older point decide "Continue" from the recorded starts alone (FR-010),** as D37.3 does, with no folder read.
9. **Unreadable settings mean online artwork lookup is off (FR-018).** Privacy wins over art.
10. **Successful discards and deletes are logged (FR-021):** the folder or game, what was removed, and how many files, so the log can answer what SavePoint deleted.
11. **Switch progress moves while git removes files (FR-029).** The dialog's share and time left follow whichever of files and bytes is further along, so a removal-heavy switch never stands at 0%.
12. **Restructures are deferred (FR-022, FR-027)** unless a fix needs the shared piece; the move's destination checks are shared with "Find the folder" (item 6).
13. **YAGNI:** the members kept only for tests or for paths no shipped build reaches go (YAGNI-001, 002, 004, 006, and 003 unless item 7's or FR-008's fix makes the path live). The 1024 px icon and IconGen's output folder stay for a future macOS package (YAGNI-005).
14. **Documentation (FR-028, FR-032's doc half)** is written in the docs pass, not by the fix agents.

## D42. Calls on the recheck of the final review's fixes

The recheck (`.tools/reviews/final-recheck.md`) found 25 of 38 items fixed and none open, plus RF-001 to RF-027 (five WARN). Each RF item is fixed as the recheck recommends, except:

1. **"Find the folder" never follows a link (RF-001, RF-002).** A candidate whose game folder, git directory, `hooks` folder, or config is a link, or resolves outside the candidate, is refused as a whole with its own error that names the path; nothing in it is written first. A repository config is rewritten whenever its content holds anything beyond `git init`'s keys and SavePoint's block, whatever its stamp, and every git process also runs with `commit.gpgSign` and `tag.gpgSign` off, so no repository setting can name a program SavePoint runs.
2. **"Update existing" is a save (RF-014).** The point it writes stops holding files the ignore list names, as every later save does (D41.2); earlier points keep them. No change.
3. **Copy never suggests emptying a folder that holds history (RF-026).** The default-folder refusal says to move that folder away to start over, or to use it through "Find the folder".
4. **The Linux package is built from Windows like the Linux gate (RF-007).** A Windows wrapper checks the commit out into the Linux-only worktree, checks it is clean with Windows git, and hands the commit to `package.sh`, which uses a commit given to it instead of asking git.
5. **YAGNI-002 is done now (recheck note):** the reason to defer it ended with the merge.

## D43. Calls on the focused review of the last fix round

The focused review (`.tools/reviews/last-round.md`) found one CRIT: a switch or a discard that writes a file where the player's disk holds a folder deletes every ignore-listed file inside it, because git treats ignored files as expendable (LR-001). It is settled as follows:

1. **A file and a folder cannot share a path, so the switch refuses rather than delete (amends D41.2).** When a switch or a discard would write a file where the work tree holds a folder that still has files the operation does not itself remove (ignore-listed or untracked), or would make a folder where the work tree holds an untracked or ignore-listed file, it refuses before it writes anything, with its own error that names the folder and the file. The player moves or deletes those files, then tries again. Silently deleting a file the player asked SavePoint to leave alone is never acceptable; leaving the profile's file unwritten would leave the install silently incomplete. The check is on the file system, not on git's rule for ignored files. A forced switch and a discard still remove plain new files inside such a folder, as they remove any new file the discard confirmation counted; they refuse over ignore-listed files and anything inside a nested repository. A switch that is not forced refuses over any new file earlier, as unsaved work.
2. **Aligning the index never loses an entry silently (LR-002).** The alignment works on a copy of the index and is compared with the original before it is swapped in; clean profile content that a named path displaces is handled under the switch journal, and any refusal puts the saved original index back as it was.
3. **"Find the folder" walks every folder of a candidate git directory for links, and every recursive walk in the engine skips links (LR-003).** A repository can gain a link after it was adopted, so the walks are link-safe on their own.
4. **A git directory cannot redirect git (LR-009).** Every git process that names a repository runs with `GIT_COMMON_DIR` set to that git directory (per call, next to `GIT_DIR`); because git still reads refs through a `commondir` file even then, every engine operation refuses a git directory that holds one, and "Find the folder" refuses a candidate whose git directory holds a `commondir` file or whose `objects/info/alternates` names a path.
5. The SUGGs (LR-004 to LR-008) are fixed as the review recommends, or skipped by the fix agent with a one-line reason.

## D44. Plain language, in-app help, an Activity screen, and a way back for removed snapshots

The owner, dogfooding v2 with a good grasp of git, still found some wording confusing, and SavePoint's players may never have heard of version control. Banning the git words was not enough: "point", "switch", "left alone", "half switched", and "storage" still described the engine instead of what a player sees. The plain-language audit (`.tools/reviews/plain-language.md`) and a sweep of v1's shipped renderer (which found v1's per-game Jobs tab missing from v2) are settled as follows; the owner chose items 1, 4, and 5.

1. **The vocabulary (owner's choice: "snapshot").** The app keeps **snapshots**: a copy of a profile's game version, mods, and saves at one moment. "Save" means only the game's own save files; SavePoint's act is "take a snapshot" in status lines, "Keep" in choices, and "Create profile" in the wizard. The rest of the audit's vocabulary table is adopted as written: "new changes" (not "unsaved changes"), "Loaded now" for every in-use marker, "Go back (to this)" as the one action on any snapshot, "Replace "X" with this session", "Continue "X" from this session", "a mix of two snapshots", "not included", "snapshot folder", "Free up space", "Files to leave out", and "game settings file". This departs from the v1 spec's copy rules as the audit lists; the History title keeps "...you can go back to". Copy may gain the placeholders the audit asks for (`{ownerName}`, `{pointName}`, `{profileName}`).
2. **Permanence is stated where the choice is made.** Anything permanent says so in the option's title or its confirm button, not only in a subtitle.
3. **The profile row counts every snapshot of the profile** (not only snapshots of the saves folder), with the ownership rule History's profile filter uses, so it matches History (a duplicate that has taken no snapshot of its own reads 0), and a profile's row menu gains **Take a snapshot now**, which is also what the no-snapshot error points to.
4. **A way back for removed snapshots (owner's choice).** Snapshots that "Continue "X" from this session" or "Replace "X" with this session" take off a profile are listed in History under **Recently removed** until the next "Free up space", and each can be brought back as a new profile. The copy says exactly that, instead of promising a recovery the app cannot do. As built: each removed snapshot is recorded before its profile moves (a record for a snapshot still on its branch is harmless and not listed), only Free up space clears the records (every Free up space, in Settings, on the Games footer, and in the library layout's game menu, asks first when it would delete some), a profile brought back and later deleted puts its snapshot back in the list until then, and a saves-only snapshot whose origin profile is gone and whose install snapshot became another profile cannot be brought back (no install to start from). This supersedes the "recoverable until the next optimization" wording in D14 and D28.
5. **An Activity screen (owner's choice).** A new rail item, Activity, lists running and recent jobs for the selected game, with an "All games" filter that also shows the jobs that span every game (a snapshot-folder move, clearing SavePoint's data). Each job shows what it is, which profile, its status, progress, and times; **Details** shows its output (file paths and steps in plain words, from the catalog, never git's own text), selectable for copying, following new lines unless the player scrolled up; **Cancel** stops a job that can be stopped. The Games screen's tab strip links to it, and the job status pill opens it. Launch and free-up-space jobs report steps too, so no job's details are empty. The Games screen footer gains **Free up space** for that game, as v1 had.
6. **In-app help.** A first-run tour of four steps (the profile row, Play, History, New profile) runs once after the first profile is made, can be skipped at every step, and can be shown again from Settings > Help. Six "What's this?" explanations open in place next to the ideas the audit names. A "How SavePoint works" page lives in Settings > Help (also F1 and a first-run link), ending with what cannot be undone. The three moments where the app explains itself at the point of decision are the first older-snapshot choice, a permanent discard, and the first Replace. All of it works by keyboard and screen reader, goes through the catalog (`help.json`), and stays out of an experienced player's way.
   As built: the page opens as a pane over the screen it covers, which is kept and comes back; the "seen" flags for the tour and the three decision notes live in the settings file (additive fields); the tour is marked seen when it starts, so closing the window mid-tour does not start it again, and it starts at most once per run; explanations shown inside a dialog leave out the link to the page; and Settings' theme, layout, and accent selectors no longer scroll themselves into view on open (Settings opened scrolled down and hid its title).
7. **Measuring it.** The owner's dogfooding is the comprehension test for now; the audit's five-player test (its OQ5) is a follow-up.
8. **No em dashes (owner's choice; replaces D39's "em dashes for asides").** Many readers take an em dash for machine-written text. An aside is a sentence of its own or a comma clause, and the catalog lint rejects an em dash anywhere. The ellipsis and the middle dot stay.

## D8. "Clear all data" is a v2 requirement

The v1 README lists "add a clear all data button" as an open TODO. v2 ships it in Settings behind a typed confirmation. It removes SavePoint's own storage, catalog, and consent markers, and never deletes or rolls back files in a game's install or save folders: whatever is on disk at that moment stays as it is.
