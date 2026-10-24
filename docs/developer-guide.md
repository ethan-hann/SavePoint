# SavePoint developer guide

How to build, run, test, and change SavePoint. What the code promises layer by layer is in `architecture.md`. Why it behaves as it does is in `decisions.md` (D1 onward). How to build a screen is in `ui-guide.md`. The working rules for every change are in `../AGENTS.md`, and planned work is in the repository's issues and milestones.

## Tools the build needs

- **The .NET 10 SDK.** `global.json` asks for 10.0.100 and rolls forward to the newest feature band; the repository is built with 10.0.401.
- **On Windows, PowerShell 7 (`pwsh`).** The build calls `build/fetch-git.ps1` through it.
- **On Linux and macOS, `bash`, `tar`, and `curl` or `wget`.**
- **Network access on the first build.** The build downloads SavePoint's own git (below). Later builds reuse it.
- **For packaging only:** `dotnet tool restore`, which installs the pinned `vpk`, and git on `PATH` (the package scripts read the commit they build).

Nothing else is needed to build and test: SavePoint never uses a git installed on the machine. Only the package scripts call git, to read the commit they build.

## Build and run

```bash
dotnet build Savepoint.slnx -c Release
```

```bash
dotnet run --project src/Savepoint.App -c Release
```

The app's assembly is `SavePoint`. On first start it shows the first-run screen.

To try it without a real game, copy `tools/FakeGame`'s build output into a folder and add that folder as a game's install folder. Set an empty folder as its saves folder. Then make it start through the program `FakeGame` with the arguments `--saves <that saves folder> --save-every 2000 --exit-after 60000`: it adds a save every two seconds and quits after a minute. Without `--exit-after` it runs until it reads `quit` on its input or is closed.

### The bundled git

`build/Git.targets` runs before every build of the app and test projects. It fetches the git bundle for the build's runtime id into `resources/git/<rid>/` (git-ignored). The bundles are:

- MinGit 2.56.0.2 for `win-x64` and `win-arm64`.
- dugite-native v2.53.0-4 for macOS and Linux.

The bundle is pinned by URL and SHA-256 in `build/git-manifest.json` and checked before extraction. It is pruned of the credential manager, LFS, and the HTTP helpers (D21, D25): SavePoint only works on local repositories. The bundle is then copied next to the build output as `git/`, so the app and the tests both run that copy. A stamp file makes every later build skip the fetch.

To use a bundle from another place, set `SAVEPOINT_GIT_ROOT` to its folder. Pick another runtime's bundle with `-p:GitRuntimeId=linux-arm64`.

### No build telemetry

`Directory.Build.targets` replaces the `AvaloniaStats` target of `Avalonia.BuildServices` with an empty one, so builds send nothing (D32). The gate scripts also set `DOTNET_CLI_TELEMETRY_OPTOUT=1` for the .NET CLI. Set it in the shell too when building by hand.

## Packaging

The version lives in one place: `<Version>` in `Directory.Build.props`. Packages are built with Velopack (D33), whose `vpk` CLI is a local tool, so run `dotnet tool restore` once.

```bash
pwsh -NoProfile -File build/package.ps1
```

- **Windows:** `build/package.ps1` publishes a self-contained `win-x64` build to `artifacts/publish/win-x64`. It checks that `git/`, `THIRD-PARTY-NOTICES.md`, and `licenses/` are there, then packs `SavePoint-win-Setup.exe` and `SavePoint-win-Portable.zip` into `artifacts/release/win-x64`. It opens each package afterwards, checks that the git bundle, the notices, and the license texts are inside, and writes the listing to `artifacts/release/contents-win-x64/`.
- **Linux:** `build/package.sh` does the same for `linux-x64` and gives `SavePoint.AppImage`. On a Linux machine, run it from a clone. Without a matching `dotnet`, `package.sh` installs a .NET SDK into its tools folder.
- **Linux package from Windows:** run `build/package-linux.ps1` (`-Commit`, default `HEAD`). Like the Linux gate, it takes the gate lock, checks the commit out into `.linux-gate`, and asks Windows git for the commit and whether the tree is clean. It then runs `package.sh --revision <commit> --tools-dir <main checkout>/.tools` under WSL, because Linux git cannot follow a worktree made by Windows git. The package lands in `.linux-gate/artifacts/release/linux-x64`.
- **Which build this is:** every package of one version has the same number, so the scripts put the commit into the informational version, and the startup log names it. A tree with uncommitted changes is refused; `-AllowDirty` (`--allow-dirty`) packs it anyway, marked `-dirty`, for a trial package.
- **Source archives:** `-Sources` (or `--sources`) also downloads the source archives of the tags `build/git-manifest.json` pins into `artifacts/release/source/`, prints their SHA-256, and compares each with the hash the manifest pins. The check runs before anything is published, and an archive is kept only when it matches. All three hashes are pinned; a download that differs fails the run. A release publishes these archives beside its packages (D21).
- **macOS:** no package is built, since that needs a Mac and signing.
- **No update checks:** the packages do not check for updates. Running a newer release's setup installs it over an older one.

The app icon in `src/Savepoint.App/Assets/Icon/` is generated by `tools/IconGen` from the app mark: the blue accent's `primary` with the `bookmark` glyph. Run it by hand when the mark changes; the colors come from the palette code, and a test pins them.

## Repository layout

| Path | What lives there |
| --- | --- |
| `src/Savepoint.Core` | Everything but the UI: `Api/` (the facade the App calls, returning `OperationResult<T>`), `Backend/` (`CoreBackend.Create`), `Services/`, `Launch/` (starting games, exit detection, the launch orchestrator), `Git/` (bundle, runner, engine), `Catalog/` (models, validation, JSON store, templates), `Paths/` (token resolution, path safety, consent markers), `Platform/`, `Errors/`. No Avalonia reference. |
| `src/Savepoint.App` | The Avalonia app: `Features/<Area>/` (view models, views, dialogs), `Composition/FeatureComposition.<Feature>.cs` (one wiring file per feature), `Hosting/AppBootstrap` (the composition root, which builds Core) and `AppComposition` (which wires the view models to the Api), `Localization/Assets/Lang/en/*.json` (all copy), `Theme/` and `Styles/` (MD3 palette, tokens, control styles), `Platform/` (notifications, single instance, reduced motion). |
| `tests/Savepoint.Core.Tests` | Core tests, most of them against real git in temp folders. |
| `tests/Savepoint.App.Tests` | View-model tests against the scriptable fake backend (`Fakes/`), headless render tests, catalog lint tests, and contract tests that run one story against the fake and the real backend (`Contract/`). |
| `tests/Shared` | Code linked into both test projects, for example `TestEnvironment`, which confines temp files to the workspace. |
| `tools/FakeGame` | A stand-in game. It writes save files on a timer (`--saves <dir> --save-every <ms>`), can hold a file open (`--lock`), starts a child the way a launcher does (`--spawn`), and exits on cue (`--exit-after <ms> --exit-code <n>`). The launch tests and manual runs use it. |
| `tools/IconGen` | Generates the app icon files from the app mark. |
| `build/` | Gate scripts, package scripts, the git fetch scripts and manifest, the test-retry targets used by the Linux gate, and `expected-skips/` (the tests each OS may skip). |
| `tools/SkipCheck` | The gate's skip check: compares the skipped tests in the xunit reports with a list in `build/expected-skips/`. Tested in `tests/Savepoint.Core.Tests/Build`. |
| State files in a game's storage | Besides the catalog and settings, two short-lived notes: `savepoint-free-up.json` (in the game's storage folder) names every record a Free up space run is deleting, so a run cut off midway is finished at the next start; `savepoint-reclaim.json` (in the git directory) names SavePoint's process and the last git it started, so the next start waits for a git a killed SavePoint left running. Each is removed when its work ends. See `architecture.md`. |
| `docs/` | This guide, `user-guide.md`, `testing-on-mac-and-linux.md`, `architecture.md`, `decisions.md`, and `ui-guide.md`. The manual dogfood checklist is on the wiki. |
| `.forgejo/workflows/` | CI: `gate.yml` runs `build/gate.sh` on Linux for every push to `main` and every pull request. |

## Tests

The test projects use xunit v3 on the Microsoft Testing Platform (`global.json`), so `dotnet test` takes the platform's options directly:

```bash
dotnet test --project tests/Savepoint.Core.Tests -c Release --filter-class Savepoint.Core.Tests.Git.Engine.CheckoutTests --max-threads 2
```

Other filters are `--filter-namespace` and `--filter-method`. Add `--report-xunit` or `--report-xunit-trx` for a report with per-test times in `bin/<config>/net10.0/TestResults/`.

- Tests never touch real user folders. Every test builds its own temp tree. A module initializer (`tests/Shared/TestEnvironment.cs`) points `TMP`, `TEMP`, and `TMPDIR` at `<repo>/.tools/tmp/<os>`, and every child process inherits them.
- Journey tests in `tests/Savepoint.App.Tests/Features/EndToEnd/RealBackend/` build the app through `AppBootstrap.Start` and `AppComposition` on temp folders, show the shell in a headless window, and walk whole user journeys with FakeGame as a real child process: first run, play, switching, older points, unsaved changes, a file in use, and storage moves. They are the closest thing to a person using the app.
- Tests that care about logging pass a `RecordingDiagnosticLog` (`tests/Shared`); no test opens a real log file, and tests that touch a log folder check first that it lies inside their own temp home.
- Opt-in smoke tests make the real OS calls: a notification, opening a link. Run them by hand with `SAVEPOINT_OS_SMOKE=1` (and `SAVEPOINT_SMOKE_URI` for the link opener).
- Headless renders (Skia, 1040x660) land in `render-output/<area>/`. Open them after a UI change and compare them with the previous render of the same screen.
- Lint tests guard the catalog:
  - every key the code uses exists, and no key is unused;
  - keys are literal strings;
  - no XAML text is hard-coded;
  - extra locales match English;
  - the copy never says "commit", "branch", "checkout", "repo", or "HEAD";
  - no aside is set off with a dash: it becomes a sentence of its own or a comma clause, and no value holds an em dash (many readers take one for machine-written text); the ellipsis and the middle dot are fine;
  - a helper line ends with a period;
  - spelling is American;
  - the terms D44 retired from the player's vocabulary ("point" as SavePoint's noun, "unsaved changes", "optimize", "capture", "marker file", and the rest) never come back; the allow-list for the few legitimate uses only shrinks, and a test fails when an allowance is no longer needed.

  The `*.defaultName` keys may keep a spaced hyphen, because they become the names people give their profiles.

### Process starts are expensive

Most Core tests run real git, and every git call starts a process. On Windows SavePoint starts the real `git.exe` under `ucrt64/bin` directly rather than through the `cmd/git.exe` launcher, which would add a second process to each call. A full run of both test projects starts tens of thousands of processes. On some Windows machines, security or monitoring software slows every process start to a second or more and keeps memory for each exited process until reboot.

In a new test, build an expensive repository state once and copy it (`tests/Savepoint.Core.Tests/Git/Engine/RepoTemplates.cs`) instead of rebuilding it with git in every test. During development, run one test class at a time.

## Gates

A change is done when the gate passes.

- **`build/gate.ps1`** (Windows) **and `build/gate.sh`** (Linux and macOS) run four steps:
  - build with `-warnaserror`;
  - run every test, writing the xunit report to `artifacts/test-results` (so `SAVEPOINT_TEST_ARGS` must not ask for `--report-xunit` itself);
  - fail on any skipped test that the OS's list in `build/expected-skips/` does not name (`tools/SkipCheck`). A broken skip probe would otherwise turn real tests into skips while the gate still passes. A listed test that now runs is printed as a note. A line starting with `~` marks a timing-dependent skip that may or may not happen. After a full, unnarrowed run, `dotnet run --project tools/SkipCheck -c Release --no-build -- --reports artifacts/test-results --expected build/expected-skips/windows.txt --write` rewrites a list from that run. macOS has no list yet, so its gate skips this step;
  - check formatting with `dotnet format --verify-no-changes`.
  
  They run at below-normal priority. They cap MSBuild at 4 nodes with no node reuse and tests at 4 threads per module, with the App and Core test modules running side by side (D48). Override the caps with `SAVEPOINT_GATE_BUILD_NODES` and `SAVEPOINT_GATE_TEST_THREADS`. Pass a report (`--report-*`) or retries (`--retry-failed-tests*`) in `SAVEPOINT_TEST_ARGS`; the gate prints them. Anything else there could run only some tests, so the gate refuses it unless `-AllowNarrowedTests` (`--allow-narrowed-tests`) says it is on purpose, and then reports the pass as narrowed.
- **`build/gate-linux.ps1`** runs the Linux gate from Windows under WSL (the `Ubuntu` distribution by default). It works from its own worktree, `.linux-gate`, so Linux and Windows build outputs never mix, and it keeps a Linux .NET SDK and NuGet caches under `.tools/`. The worktree lives on the Windows drive, which WSL mounts over 9p, and that mount can briefly hide a file that was just renamed into place. So the Linux gate retries a failed test up to twice and lists every retry (D27). The Windows gate never retries. The App tests load the Linux Skia build that needs no fontconfig, so the gate needs nothing installed in the distribution. Tests of what the 9p mount cannot do (Unix sockets, permission changes, case-sensitive names) probe for it and skip. The gate warns at the start when permissions do not hold. So more tests skip under WSL than in CI, and the WSL gate checks its skips against its own list (`linux-wsl.txt`).
- **One gate at a time.** Every gate takes a machine-wide lock (`<main checkout>/.tools/locks/gate.lock`), so gates started from different worktrees run one after another.
- **macOS is built and unit-tested only through the shared code paths.** It has not been verified on hardware (D9).

## Rules for changes

- **Test first.** For every behavior, write the test and watch it fail on an assertion for the expected reason; a compile or harness error does not count. Then write the code. A test that passes on its first run gets a mutation check before it counts. Record the observed failure in the commit body.
- **Copy lives in the catalog only.** Every user-visible string goes through `Localization/Assets/Lang/en/<area>.json`. A key enters the catalog together with the code that uses it. Error copy is keyed by the error's message id, and every `error.*` id has an `ErrorPresentation` entry (icon, tone, title).
- **Docs and comments.** Public Core APIs carry XML doc comments that state their contract; a missing one fails the Core build (CS1591, D24). Inline comments are short, say why, and never name a milestone, task, or review. A comment may cite a decision (`D29`), because decisions are lasting design records in `decisions.md`.
- **Core stays testable.** It holds no static mutable state and has no UI dependency. Side effects tests need to fake go through injected abstractions. Async methods take a `CancellationToken` all the way down.
- **Departures are decisions.** A change that departs from the spec, or fills a gap in it, gets a decision in `decisions.md`.
- **Cross-platform.** Windows, Linux, and macOS run from one target framework. OS-specific code is guarded with `OperatingSystem.Is*()` (D26).

## Adding a language

SavePoint ships English only. The catalogs are embedded in the app when it is built, so a language is added to the source tree and shipped in a new build; a file dropped next to an installed app is never read (D39).

1. Copy `src/Savepoint.App/Localization/Assets/Lang/en/` to `Lang/<code>/`, where `<code>` is a language tag such as `de` or `pt-BR`. The files may be split differently from English, but together they must hold exactly the English keys.
2. Translate the values. They are ICU MessageFormat: keep every `{argument}` name as it is, and give `plural` messages the categories the language needs. Plural rules and number formats follow the tag.
3. Build and run the catalog lint tests (`CatalogLintTests`). They report a malformed tag, a missing or extra key, different argument names, and a message that does not parse, each with the locale and the key.
4. Settings has no language list yet, because there is only one language. A second locale folder makes `LanguageAvailabilityTests` fail on purpose: it marks where Settings must gain a list built from `LocalizationCatalog.EmbeddedLocales`, with the choice saved in the settings file.

## Where to start reading

1. `docs/architecture.md`: the layers and the error model.
2. `src/Savepoint.Core/Backend/CoreBackend.cs`: how the backend is assembled.
3. `src/Savepoint.Core/Git/GitEngine*.cs`: what a capture, a switch, and a discard do to the folders, and the safety rules D28 and D29.
4. `src/Savepoint.App/Hosting/AppBootstrap.cs` and `src/Savepoint.App/Features/Shell/`: how the app starts and navigates.
