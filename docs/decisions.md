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

## D8. "Clear all data" is a v2 requirement

The v1 README lists "add a clear all data button" as an open TODO. v2 ships it in Settings behind a typed confirmation. It removes SavePoint's own storage, catalog, and consent markers, and never deletes or rolls back files in a game's install or save folders: whatever is on disk at that moment stays as it is.
