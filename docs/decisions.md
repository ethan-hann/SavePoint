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

On launch, before any screen: verify the bundled git (`git --version`); failure shows a dedicated error screen naming the path and what to reinstall, never a silent failure. Stale `index.lock` files under the storage root are removed when they predate the current process (SavePoint is single-instance and owns every git process on its own repos); during a run, a lock is removed only under the game's lock with no SavePoint git process in flight for that repo. A corrupt catalog file shows the recovery screen from D13.2.

## D19. Onboarding and editor guidance from the plan's risk table

- First run and the game editor say plainly that games with kernel anti-cheat are not supported (the plan's explicit non-goal), without attempting detection.
- The game editor recommends turning off the storefront's cloud saves for a managed game (Steam Cloud, GOG Galaxy), next to the launch-method choice.
- Long paths: `core.longpaths=true` everywhere, and the engine's tests include a tree deeper than 260 characters on Windows.

## D20. Translator workflow

The v1 FormatJS extraction step becomes two tests plus a short guide: every key referenced from XAML or C# exists in `en.json`, `en.json` has no unreferenced keys, and every additional catalog (`<locale>.json`) has exactly the same keys with ICU arguments that match English. Adding a language is dropping a file next to `en.json`; the locale list in Settings is driven by the files present.

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

- **A game folder's own `.gitignore` files are not honored.** SavePoint must restore exactly what was on disk, so only SavePoint's exclude list (`info/exclude`: the marker and the game's `ignorePaths`) decides what is skipped. Capture lists files itself (`ls-files -z -o -m -d --exclude-from=<gitDir>/info/exclude`, never `--exclude-standard`) and stages them with `add -f --pathspec-from-file=- --pathspec-file-nul`; status/unsaved-change detection and capture previews use the same listing; clean uses `clean -fdx` with SavePoint's patterns passed as `-e`, so a file a mod's `.gitignore` names is still saved, still detected as unsaved, and still cleaned on discard.
- **Nested version-control folders are refused, not half-saved.** A folder inside the game that holds its own `.git` (a mod cloned from GitHub) would otherwise be stored as an empty pointer whose files never come back, or fail the capture outright. Capture and preview detect any nested `.git` directory or file and refuse with `error.capture.nestedRepository`, naming the folder, before staging anything. The user moves the mod's hidden history folder out and captures again.
- **Two optimization modes** (refines D25). *Routine* (`repack -d --geometric=2`) runs automatically after captures and never rewrites the large packs. *Reclaim* expires reflogs, then `repack -a -d` and `prune --expire=now`, dropping everything no profile reaches: it runs when the user presses "Optimize storage" and automatically after a profile or game is deleted (preemptible like any optimization, D13.17). So D17's "freed by the next optimization" and D14's "recoverable until the next optimization" hold. Repos keep `core.logAllRefUpdates=false`; reflogs add nothing for SavePoint.
- **Profile branch names are always lowercase** (`CatalogIds`), so case-only collisions on case-insensitive file systems cannot happen.
- **Symbolic links inside game folders** are stored as links on Linux and macOS (git default) and as plain files on Windows (`core.symlinks=false`); a game that depends on symlinks on Windows is a documented limitation.

## D8. "Clear all data" is a v2 requirement

The v1 README lists "add a clear all data button" as an open TODO. v2 ships it in Settings behind a typed confirmation. It removes SavePoint's own storage, catalog, and consent markers, and never deletes or rolls back files in a game's install or save folders: whatever is on disk at that moment stays as it is.
