# SavePoint developer guide

How to build, run, test, package, and release SavePoint. The working rules for every change are in `../AGENTS.md`. Layer contracts are in `architecture.md`, design reasons in `decisions.md` (D1 onward), and screen building in `ui-guide.md`. Planned work is in the repository's issues and milestones.

## Tools the build needs

- **The .NET 10 SDK.** `global.json` asks for 10.0.100 and rolls forward to the newest feature band.
- **On Windows, PowerShell 7 (`pwsh`).** The build calls `build/fetch-git.ps1` through it.
- **On Linux and macOS, `bash`, `tar`, and `curl` or `wget`.** No JSON tool is necessary.
- **Network access on the first build**, to download SavePoint's own git. Later builds reuse it.
- **For the full test run:** PowerShell 7 on every OS, `gpg`, and on Linux `dbus-daemon`. On Windows, `gpg` comes from Gpg4win or from Git for Windows' `usr\bin`. On the Linux VM, `build/gate-linux.sh` installs the .NET SDK and PowerShell into its own folder. If `rsync`, `flock`, `dbus-daemon`, or `gpg` is missing, it stops with the `apt install` line to run. A test that skips because one of these is missing fails the gate.
- **For packaging only:** `dotnet tool restore`, which installs the pinned `vpk`, and git on `PATH`.

SavePoint never uses a git installed on the machine. Only the package scripts call git, to read the commit they build.

## Build and run

```bash
dotnet build Savepoint.slnx -c Release
```

```bash
dotnet run --project src/Savepoint.App -c Release
```

The app's assembly is `SavePoint`. On first start it shows the first-run screen.

To try it without a real game, copy `tools/FakeGame`'s build output into a folder and add that folder as a game's install folder, with an empty folder as its saves folder. Start the program `FakeGame` with the arguments `--saves <that saves folder> --save-every 2000 --exit-after 60000`. It adds a save every two seconds and quits after a minute. Without `--exit-after` it runs until it reads `quit` on its input or is closed.

### The bundled git

SavePoint runs its own copy of git: MinGit on Windows (`win-x64`, `win-arm64`) and dugite-native on macOS and Linux, pruned of what a local-only app never uses (D21, D25). `build/Git.targets` runs before every build of the app and test projects. It fetches the bundle for the build's runtime id into `resources/git/<rid>/` (git-ignored), checks it against the URL and SHA-256 pinned in `build/git-manifest.json`, and copies it next to the build output as `git/`. The app and the tests both run that copy. A stamp file makes later builds skip the fetch.

To use a bundle from another place, set `SAVEPOINT_GIT_ROOT` to its folder. Pick another runtime's bundle with `-p:GitRuntimeId=linux-arm64`.

### No build telemetry

Builds send nothing (D32). The gate scripts set `DOTNET_CLI_TELEMETRY_OPTOUT=1` for the .NET CLI. Set it in your shell too when you build by hand.

### The app icon

`tools/IconGen` generates the app icon in `src/Savepoint.App/Assets/Icon/` from the app mark. Run it by hand when the mark changes, and commit the new files with the change. Tests render every frame with the generator's code and compare it with the committed files, so a generator change without new files fails.

## Tests

The test projects use xunit v3 on the Microsoft Testing Platform (`global.json`), so `dotnet test` takes the platform's options directly:

```bash
dotnet test --project tests/Savepoint.Core.Tests -c Release --filter-class Savepoint.Core.Tests.Git.Engine.CheckoutTests --max-threads 2
```

Other filters are `--filter-namespace` and `--filter-method`. Add `--report-xunit` or `--report-xunit-trx` for a report with per-test times in `bin/<config>/net10.0/TestResults/`.

- **Temp folders.** Tests never touch real user folders. Every test builds its own temp tree. A module initializer (`tests/Shared/TestEnvironment.cs`) points `TMP`, `TEMP`, and `TMPDIR` at `<repo>/.tools/tmp/<os>`, and every child process inherits them.
- **Journey tests** in `tests/Savepoint.App.Tests/Features/EndToEnd/RealBackend/` build the real app through `AppBootstrap.Start` and `AppComposition` on temp folders, in a headless window, and run FakeGame as a real child process.
- **Logging.** Tests that care about logging pass a `RecordingDiagnosticLog` (`tests/Shared`). No test opens a real log file, and a test that touches a log folder first checks that it lies inside its own temp home.
- **OS smoke tests** make the real OS calls (a notification, opening a link). They are opt-in. Run them by hand with `SAVEPOINT_OS_SMOKE=1`, and `SAVEPOINT_SMOKE_URI` for the link opener.
- **Headless renders** (Skia, 1040x660) go to `render-output/<area>/` when `SAVEPOINT_RENDER=1` is set; without it the render-only tests skip (a few view tests still draw while they check, #257). Render the screen before you change it as well as after, since a fresh worktree has no previous render; then compare the two pictures.
- **Guide images.** The headless renderer draws the images in `docs/images` from one seeded data set (`tests/Savepoint.App.Tests/Features/DocScreenshots/`: a game called Grounded, its profiles and snapshots, a fixed clock, light theme, Blue accent, 1280x800), so every run gives the same bytes. After a change to a screen they show, regenerate them and commit them with the change:

  ```bash
  SAVEPOINT_DOC_SCREENSHOTS=1 dotnet test --project tests/Savepoint.App.Tests -c Release --filter-class Savepoint.App.Tests.Features.DocScreenshots.DocScreenshotTests --max-threads 2
  ```

  In PowerShell, set `$env:SAVEPOINT_DOC_SCREENSHOTS = "1"` first. Without the variable the job skips, so the gate never rewrites the docs. An always-on test fails when the user guide or the README shows an image the job does not draw, or the job draws one that nothing shows. A new screen in the guide gets a generator in `DocScreenshotJob`.
- **Catalog lint tests** fail on a missing or unused key, a key that is not a literal string, hard-coded XAML text, an extra locale that does not match English, a helper line without a final period, and British spelling. They also fail when the copy:
  - says "commit", "branch", "checkout", "repo", or "HEAD";
  - sets off an aside with a dash instead of a sentence of its own or a comma clause, or holds an em dash (the ellipsis and the middle dot are fine). The `*.defaultName` keys may keep a spaced hyphen, because they become the names people give their profiles;
  - uses a term D44 retired from the player's vocabulary ("point" as SavePoint's noun, "unsaved changes", "optimize", "capture", "marker file", and the rest). The allow-list for the few legitimate uses only shrinks, and a test fails when an allowance is no longer needed.

### Process starts are expensive

Most Core tests run real git, and every git call starts a process. On some Windows machines, security or monitoring software makes every process start slow. In a new test, build an expensive repository state once and copy it (`tests/Savepoint.Core.Tests/Git/Engine/RepoTemplates.cs`) instead of rebuilding it with git each time. During development, run one test class at a time.

## Gates

A change is done when the gate passes. `build/gate.ps1` (Windows) and `build/gate.sh` (Linux and macOS) run four steps:

1. Build with `-warnaserror`.
2. Run every test, in four processes at once: Core, and the App tests split into the lanes `build/app-test-lanes.txt` names (EndToEnd, the real-backend journeys, and everything else) so each lane gets its own headless UI thread. With less than 10 GB of memory available at the start (inside a container, the smaller of that and its memory limit), or when the memory cannot be read, the App tests run as one process instead, and the gate says which it chose; `SAVEPOINT_GATE_APP_LANES=1` or `3` forces one way. Each line of output carries its process's name as it happens, and a "still running" line names the busy processes every five minutes (`SAVEPOINT_GATE_HEARTBEAT_SECONDS`). On Linux the gate gives the test processes `artifacts/tmp` on disk as their temp folder until each test process moves its own to `.tools/tmp/<os>` (see Temp folders), so nothing lands in a RAM-backed `/tmp`. A count check (`build/check-test-counts.ps1` and `.sh`) fails the gate if the lanes' tests differ from the full App test list, so a new namespace is never dropped (D70). Each process writes its xunit report to `artifacts/test-results`, so `SAVEPOINT_TEST_ARGS` must not ask for `--report-xunit` itself.
3. Fail on a skipped test that the OS's list in `build/expected-skips/` does not name (`tools/SkipCheck`, D48). A listed test that now runs is printed as a note. A line that starts with `~` marks a timing-dependent skip. macOS has no list yet, so its gate skips this step. After a full, unnarrowed run, `dotnet run --project tools/SkipCheck -c Release --no-build -- --reports artifacts/test-results --expected build/expected-skips/windows.txt --write` rewrites a list from that run.
4. Check formatting with `dotnet format --verify-no-changes`. The Windows CI job passes `-SkipFormat`, since the Linux job checks the same thing (D70); local gates always check it.

The gates run at below-normal priority. They cap MSBuild at 4 nodes with no node reuse, and tests at a third of the processors for Core (4 to 8 threads; at most 4 when the App tests run as one process) and 2 for each App lane, all side by side (D48, D70). Override the caps with `SAVEPOINT_GATE_BUILD_NODES` and `SAVEPOINT_GATE_TEST_THREADS`.

`SAVEPOINT_TEST_ARGS` takes only a report (`--report-*`), and the gate prints it. The gate refuses anything else there: a filter could run only some tests, and test retries are not available (D60). A narrowed run is allowed when `-AllowNarrowedTests` (`--allow-narrowed-tests`) says it is on purpose, and the gate reports its pass as narrowed.

- **`build/gate-linux.ps1 -Commit <branch or commit>`** runs the Linux gate from Windows on the Linux test VM over SSH, with a key only (D60). It sends the committed tree of that commit (default `HEAD`), so a local branch that was never pushed works. It does not gate uncommitted changes, and it says so.
  - On the VM it works in `~/gate`, with its own .NET SDK, PowerShell and NuGet caches, and keeps build outputs between runs. The gate checks the VM's skips against `linux-ci.txt`, the same list as CI.
  - The exit code is the gate's. Exit 75 means the VM is busy: another gate holds `~/gate/gate.lock`, or a gate or package run holds the shared `~/.savepoint-vm/heavy.lock` (D60.5). A lock whose holder is gone, or that predates the VM's last boot, is stale: the next run ends the leftover processes holding it and takes it (D60.6). The log comes back to `.tools/gate-linux-last.log`.
  - The VM's address, user, key and folder default to the test VM's and can be changed with `-VmHost`, `-VmUser`, `-VmKey`, `-VmDir`, or `SAVEPOINT_VM_HOST`, `SAVEPOINT_VM_USER`, `SAVEPOINT_VM_KEY`, `SAVEPOINT_VM_DIR`.
  - A full run takes about 16 minutes on the 4-core VM.
- **One gate at a time.** Every Windows gate takes a machine-wide lock (`<main checkout>/.tools/locks/gate.lock`), so gates started from different worktrees run one after another. The VM gate locks the VM, so it can run beside a Windows gate but not beside a VM package run.
- **macOS** is built and unit-tested only through the shared code paths. It has not run on hardware (D9).

## Rules for changes

The rules in `../AGENTS.md` apply to every change. These add to them:

- **Catalog.** Copy lives in `Localization/Assets/Lang/en/<area>.json`. A key enters the catalog with the code that uses it. Error copy is keyed by the error's message id, and every `error.*` id has an `ErrorPresentation` entry (icon, tone, title).
- **Doc comments.** A missing XML doc comment on a public Core API fails the Core build (CS1591, D24). Inline comments never name a milestone, slice, issue, task, review, pull request, or build step. A comment may cite a decision (`D29`), because decisions are lasting records in `decisions.md`.
- **Core stays testable.** It has no UI dependency, and no static mutable state except a handle to an OS resource that must live as long as the process (D59). The clock, the environment, other processes, disk space, and the network come in through injected seams. Api reads take a `CancellationToken`. A long job is cancelled through `IJobsApi.CancelAsync`, and the job's own token flows all the way down through its work. A short mutation takes no token, because stopping it halfway is worse than finishing it.
- **Departures are decisions.** A change that departs from the spec, or fills a gap in it, gets a decision in `decisions.md`.

## Adding a language

SavePoint ships English only. The catalogs are embedded at build time, so add a language to the source tree and ship it in a new build. The app never reads a file dropped next to an installed copy (D39).

1. Copy `src/Savepoint.App/Localization/Assets/Lang/en/` to `Lang/<code>/`, where `<code>` is a language tag such as `de` or `pt-BR`. The files may be split differently from English, but together they must hold exactly the English keys.
2. Translate the values. They are ICU MessageFormat. Keep every `{argument}` name as it is, and give `plural` messages the categories the language needs. Plural rules and number formats follow the tag.
3. Build and run the catalog lint tests (`CatalogLintTests`). They report a malformed tag, a missing or extra key, different argument names, and a message that does not parse, each with the locale and the key.
4. Add a language list to Settings, which has none while there is one language. A second locale folder makes `LanguageAvailabilityTests` fail on purpose, to mark the place. Build the list from `LocalizationCatalog.EmbeddedLocales`, and save the choice in the settings file.

## Packaging

The version lives in one place: `<Version>` in `Directory.Build.props`. Packages are built with Velopack (D33). Its `vpk` CLI is a local tool, so run `dotnet tool restore` once.

```bash
pwsh -NoProfile -File build/package.ps1
```

- **Windows:** `build/package.ps1` publishes a self-contained `win-x64` build to `artifacts/publish/win-x64` and packs `SavePoint-win-Setup.exe` and `SavePoint-win-Portable.zip` into `artifacts/release/win-x64`. Before and after the pack it checks that `git/`, `THIRD-PARTY-NOTICES.md`, and `licenses/` are there, and it writes each package's listing to `artifacts/release/contents-win-x64/`.
- **Linux:** `build/package.sh` does the same for `linux-x64` and gives `SavePoint.AppImage`. Run it from a clone on a Linux machine. Without a matching `dotnet`, it installs a .NET SDK into its tools folder.
- **Linux package from Windows:** run `build/package-linux.ps1` (`-Commit`, default `HEAD`). Like the Linux gate, it sends the committed tree to the Linux VM and runs `package.sh` there, in its own folder (`~/package`) under its own lock and the VM's shared one, so it never runs beside a VM gate. The package comes back to `artifacts/release/linux-x64`, with its listing beside it.
- **Which build this is:** the scripts put the commit into the informational version, and the startup log names it. `build/package.ps1` and `build/package.sh` refuse a tree with uncommitted changes; `-AllowDirty` (`--allow-dirty`) packs it anyway, marked `-dirty`, for a trial package. `build/package-linux.ps1` always packs the committed tree of the commit it is given.
- **macOS:** no package is built (D33).
- **No update checks:** the packages do not check for updates (D33). Running a newer release's setup installs it over an older one.

### Source archives

`-Sources` (or `--sources`) also downloads the sources a release must offer for the bundled git (D21):

- the source archives of the tags `build/git-manifest.json` pins, into `artifacts/release/source/`;
- the source of every MSYS2 and MINGW package in the Windows bundle, into `artifacts/release/source/packages/`, each pinned with its hash in `build/msys2-sources.json`.

Each download is checked against its pinned SHA-256 before anything is published. A mismatch fails the run. An archive already there that matches its pin is reused, and a file in that folder that the list no longer names is removed first. The shared steps live in `build/package-sources.sh` and `build/package-sources.ps1`.

**After a MinGit update**, regenerate the MSYS2 list. On Windows, where the bundle is built, a test fails until you do (it skips elsewhere). Run `pwsh build/msys2-sources.ps1 -Action Generate` (options `-BundleDirectory`, `-OutFile`, `-CacheDirectory`, `-Date`). It reads the bundle's `etc/package-versions.txt`, looks each package up in Git for Windows' pacman-repo releases and then on repo.msys2.org, and downloads into the package script's cache. It checks each archive's `.sig` with `gpg` against the key pinned for its host in `build/keys` (the MSYS2 development key and the Git for Windows release key, which expires on 2027-03-23). A package it cannot find, or a bad or missing signature, stops the run with nothing written. `-SkipSignatures` skips the check with a warning.

**Publish the archives once per bundled git version**, as a release of their own tagged `git-sources-<MinGit version>` (for example `git-sources-2.56.0.2`). Each app release links to it in its notes and as a row in its downloads list (a release attachment with an `external_url` that points at the sources release). A release that changes the pinned bundles publishes a new sources release first.

### Releases

1. Push a tag `v<Version>`. It runs `.forgejo/workflows/release.yml`.
2. `build/release-assets.ps1 -Action Verify` refuses a tag that differs from `<Version>`, a release for the tag that is already published, or a missing `git-sources-<MinGit version>` release.
3. The Linux job runs `build/package.sh`, and the Windows job (the self-hosted Windows runner) runs `build/package.ps1`, both without sources. Each uploads its folder to a draft pre-release for the tag. Large files go up as `.partN` pieces. A re-run replaces the assets.
4. A last job writes `SHA256SUMS.txt` (one line per file and per piece, plus the joined file) and adds the "Source code of the bundled git" link.
5. To finish a run by hand, run `pwsh build/release-assets.ps1 -Action Checksums -Tag v<Version>` with `RELEASE_TOKEN`, `GITHUB_REPOSITORY` and `GITHUB_SERVER_URL` set.
6. Read the draft, add the notes, and publish it by hand. The workflow never publishes.
7. Publishing runs `.forgejo/workflows/github-mirror.yml`, which copies the release (name, notes, prerelease flag and every file) to the GitHub mirror with `build/mirror-release.ps1` (D71). To sync a release you edited later, or one the event missed, run that workflow by hand from the Actions page with the tag, or `all`.

**Once, by the owner:** create a fine-grained GitHub token limited to `ethan-hann/SavePoint` with Contents read and write, add it as the Forgejo repository secret `GH_MIRROR_TOKEN`, then run the GitHub mirror workflow with the tag `all` to copy the existing releases.

Only the upload steps get a token: the repository secret `RELEASE_TOKEN` when it is set, else the workflow's own token. If the first run's upload is refused, add `RELEASE_TOKEN` (an access token with write access to the repository).

## Documentation site

DocFX, a local tool, builds the guides in `docs/` and an API reference from `Savepoint.Core`'s doc comments into one static site (D55):

```bash
dotnet tool restore
dotnet docfx docfx.json --serve
```

That builds into `artifacts/docs-site` and serves it at http://localhost:8080. `docfx.json` names the sources, `docs/toc.yml` orders the guides, and `docfx/` holds the home page, the API landing page and `template/public/main.css`. The stylesheet holds the Material roles the app derives from the default Blue accent. If the default accent changes, compute the roles again from `Md3Palette.Build` and update the stylesheet.

### Publishing

`.forgejo/workflows/docs.yml` builds the site with warnings as errors on every pull request that touches its sources, so a broken link or a `docs/toc.yml` entry for a renamed or removed guide fails there. On every push to `main` that changes the sources, it builds the site and uploads it to a git-pages server. Tags do not publish: a tag points at a commit already on `main`, which published it. It can also be started by hand. A change to the guides alone shows on the site minutes after it merges, and needs only the gate's short documentation path (see `.forgejo/workflows/` under Repository layout).

The host is shared with other projects. Each publishes under the path named like its repository, so SavePoint's site is `https://docs.tortal.tech/SavePoint/` (`DOCS_PATH` in the workflow), and the `docs-index` repository publishes the front page. The workflow refuses an empty `DOCS_PATH`, which would aim at the front page. Publishing needs the repository variable `DOCS_SITE_URL`, the root of that host. Without it the job builds and stops.

The upload needs no secret. The server maps `docs.tortal.tech/<name>/` to `git.tortal.tech/ethan-hann/<name>.git` and accepts a workflow token that may push to that repository. So a project can publish only its own site, and a new project needs only the workflow.

The server's one-time setup (the git-pages container, its wildcard config, the proxy host and DNS) is a maintainer task outside this guide.

## Repository layout

| Path | What lives there |
| --- | --- |
| `src/Savepoint.Core` | Everything but the UI: `Api/` (the facade the App calls, returning `OperationResult<T>`), `Backend/` (`CoreBackend.Create`), `Services/`, `Launch/` (starting games, exit detection, the launch orchestrator), `Git/` (bundle, runner, engine), `Catalog/` (models, validation, JSON store, templates), `Paths/` (token resolution, path safety, consent markers), `Platform/`, `Errors/`. No Avalonia reference. |
| `src/Savepoint.App` | The Avalonia app: `Features/<Area>/` (view models, views, dialogs), `Composition/FeatureComposition.<Feature>.cs` (one wiring file per feature), `Hosting/AppBootstrap` (the composition root, which builds Core) and `AppComposition` (which wires the view models to the Api), `Localization/Assets/Lang/en/*.json` (all copy), `Theme/` and `Styles/` (MD3 palette, tokens, control styles), `Platform/` (notifications, single instance, reduced motion). |
| `tests/Savepoint.Core.Tests` | Core tests, most of them against real git in temp folders. |
| `tests/Savepoint.App.Tests` | View-model tests against the scriptable fake backend (`Fakes/`), headless render tests, catalog lint tests, and contract tests that run one story against the fake and the real backend (`Contract/`). |
| `tests/Shared` | Code linked into both test projects, for example `TestEnvironment`, which confines temp files to the workspace. |
| `tools/FakeGame` | A stand-in game for the launch tests and manual runs. It writes save files on a timer (`--saves <dir> --save-every <ms>`), can hold a file open (`--lock`), starts a child the way a launcher does (`--spawn`, last on the line, with `--linger <ms>` before it to keep the starter running that long after the child starts), and exits on cue (`--exit-after <ms> --exit-code <n>`). `--exit-unless-env <name>` makes it quit at once unless that environment variable is set, like a game that needs its store's client running (D67). |
| `tools/IconGen` | Generates the app icon files from the app mark. |
| `build/` | Gate scripts, package scripts, the git fetch scripts and manifest, and `expected-skips/` (the tests each OS may skip). `BuildScriptSyntaxTests` in `tests/Savepoint.Core.Tests/Build` parses every `.ps1` with the PowerShell parser and checks every `.sh` with `bash -n`, so a quoting slip fails the gate. |
| `tools/PackageSources` | Reads `build/msys2-sources.json` (`archives`) and the sources of `build/git-manifest.json` (`tags`) for the packaging scripts, and one runtime's bundle (`bundle <manifest> <rid>`) for `build/fetch-git.sh`. The layout of either file does not matter. The bash fetch takes a short lock (`.tools/downloads/manifest-read.lock.d`) around the read. Tested in `tests/Savepoint.Core.Tests/Build`. |
| `tools/SkipCheck` | The gate's skip check: compares the skipped tests in the xunit reports with a list in `build/expected-skips/`. Tested in `tests/Savepoint.Core.Tests/Build`. |
| `docs/` | This guide, `user-guide.md`, `testing-on-mac-and-linux.md`, `architecture.md`, `decisions.md`, `ui-guide.md`, `dogfood-checklist.md` (the manual checks), and `toc.yml` (their order on the documentation site). |
| `docfx.json`, `docfx/` | The documentation site (see Documentation site): its sources, home page and stylesheet. |
| `.agents/`, `.han/`, `.claude/rules/` | Instructions for people and agents changing the code, kept out of `docs/` and the documentation site: coding standards, the Han skills' settings, and the index files that point Claude Code at the standards. `AGENTS.md` describes them. |
| `.forgejo/workflows/` | CI. `gate.yml` runs `build/gate.sh` on Linux (in a container) and `build/gate.ps1` on the self-hosted Windows runner (label `windows`) for every pull request, and can be started by hand. Pushes to `main` do not run it: `main` takes fast-forward merges only, so what lands is the commit its pull request already passed. A change to documentation or agent instructions only (`docs/`, `docfx.json`, `docfx/`, root Markdown files other than `THIRD-PARTY-NOTICES.md`, `.agents/`, `.claude/rules/`, and `.han/`) passes without them. `release.yml` builds the packages on a `v*` tag, and `github-mirror.yml` copies a published release to GitHub (see Releases). `docs.yml` builds the documentation site and publishes it from `main` (see Documentation site). |

## Where to start reading

1. `docs/architecture.md`: the layers and the error model.
2. `src/Savepoint.Core/Backend/CoreBackend.cs`: how the backend is assembled.
3. `src/Savepoint.Core/Git/GitEngine*.cs`: what a capture, a switch, and a discard do to the folders, and the safety rules D28 and D29.
4. `src/Savepoint.App/Hosting/AppBootstrap.cs` and `src/Savepoint.App/Features/Shell/`: how the app starts and navigates.
