# Working on SavePoint

Rules for anyone changing this repository, human or agent. Claude Code reads this file through `CLAUDE.md`, and other agents read it directly. Product docs are in `docs/`: start with `docs/developer-guide.md` (build, test, package), then `docs/architecture.md`, `docs/decisions.md` (every design call, D1 onward), and `docs/ui-guide.md` for screens. Planned work is tracked as issues and milestones on https://git.tortal.tech/ethan-hann/SavePoint.

SavePoint is licensed GPL-3.0-or-later (`LICENSE`). Bundled components keep their own licenses (`THIRD-PARTY-NOTICES.md`, `licenses/`). A new dependency needs a license compatible with GPL-3.0 and an entry in the notices.

## Project Discovery

- Default branch: `main`
- Docs: `docs/`, user and developer guides only, published to https://docs.tortal.tech/SavePoint by `docfx.json`. Agent material never goes here.
- Agent docs: `.agents/` (instructions for people and agents changing the code; not published)
- ADRs: none. Decisions are numbered entries in `docs/decisions.md` (see Decisions and planned work)
- Coding standards: `.agents/coding-standards/`, indexed for Claude Code by area (Core, App, tests, tools) in `.claude/rules/coding-standards/`
- Layout: `src/Savepoint.Core/` (UI-free library: `Git/` snapshot storage, `Catalog/`, `Backend/`, `Launch/`); `src/Savepoint.App/` (Avalonia app: screens in `Features/<Area>/`, each `*View.axaml` beside its `*ViewModel.cs`, DI wiring in `Composition/`); `tests/Savepoint.Core.Tests/` and `tests/Savepoint.App.Tests/` mirror their projects, with shared helpers in `tests/Shared/`; `tools/` (`FakeGame` and `SkipCheck` serve the tests, plus `IconGen` and `PackageSources`); `build/` (gate, packaging, and bundled-git scripts, `expected-skips/`). The full table is under Repository layout in `docs/developer-guide.md`.
- Language: C# (`LangVersion` latest) on `net10.0`, .NET SDK 10.0.100 (`global.json`)
- Package manager: NuGet with central package management (`Directory.Packages.props`); one solution, `Savepoint.slnx`
- Frameworks: Avalonia 12, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite (no ORM), xunit.v3 on Microsoft.Testing.Platform, Avalonia.Headless, Velopack, Serilog
- Install: the .NET 10 SDK, then `dotnet tool restore` (vpk, docfx) for packaging and the docs site
- Build: `dotnet build Savepoint.slnx -c Release`
- Test one class: `dotnet test --project tests/Savepoint.Core.Tests -c Release --filter-class <Full.Class.Name> --max-threads 2`
- Full check: `build/gate.ps1` (Windows) or `build/gate.sh` (Linux, macOS); see Gates
- Lint: `dotnet format Savepoint.slnx --verify-no-changes`
- Dev: `dotnet run --project src/Savepoint.App -c Release`
- Package: `build/package.ps1` (Windows), `build/package.sh` (Linux)
- Docs site: `dotnet docfx docfx.json --serve`
- Han output: `.han/out/` (ignored by `.han/.gitignore`; set in `.han/config.md`)

## Gates

- Run `build/gate.ps1` (Windows) or `build/gate.sh` (Linux, macOS). It builds with warnings as errors, runs every test (Core and three App processes side by side, one App process when less than 10 GB of memory is free, with a count check that no App test is dropped), and checks formatting. A change is done when the gate passes. In CI only the Linux job checks formatting; the Windows job passes `-SkipFormat` (D70).
- CI runs the Linux gate and the Windows gate on every pull request, and by hand (`.forgejo/workflows/gate.yml`). Pushes to `main` do not run it, because `main` takes fast-forward merges of commits that already passed. The Windows job runs on a self-hosted Windows VM.
- A change that touches only documentation or agent instructions passes without building or testing, and needs no local gate. That means `docs/`, the documentation site's `docfx.json` and `docfx/`, Markdown files at the root except `THIRD-PARTY-NOTICES.md`, and `.agents/`, `.claude/rules/`, and `.han/`. `.forgejo/workflows/docs.yml` builds the site for a documentation change instead.
- Run `build/gate.ps1` locally on the branch, and see it pass, before opening a pull request or pushing more commits to one. The CI gate on the Windows VM costs far more than a local run, so a failure must show up locally first. Only a change that skips the gate in CI (above) skips this run.
- Both CI gates are required checks on `main`, which takes fast-forward merges only. A pull request cannot merge while the Windows runner is offline; it waits for the runner.
- `SAVEPOINT_TEST_ARGS` takes only a report (`--report-*`); test retries are not available. A filter is refused unless `-AllowNarrowedTests` / `--allow-narrowed-tests` says it is on purpose. A narrowed run is not a gate.

## Code

- Write tests first for every behavior. A red must be an assertion failure for the expected reason, not a compile or harness error. A test that passes on its first run gets a mutation check before it counts. Record the observed red in the commit message.
- Send every user-visible string through the catalog (`src/Savepoint.App/Localization/Assets/Lang/en/*.json`). Keys are literal. The catalog never says "commit", "branch", "checkout", "repo", or "HEAD", uses no em dashes, and states permanence in the title or the button (D44). Lint tests enforce this.
- Use the D44 vocabulary (snapshot, profile, Go back, Recently removed, Free up space). Explain a new concept in plain words, with a question button (such as "What's a snapshot?") when it needs one.
- Keep inline comments short and say why. Never name a milestone, slice, issue, task, review, pull request, or build step in them.
- Give public Core APIs XML doc comments that state their contract. CS1591 is an error in Core.
- Tests never create, write, delete, or list anything in a real user folder. Each test uses a fresh temp directory and injected app-data and storage paths.
- Stay cross-platform: Windows, Linux, and macOS run from one target framework (D9, D26). Per-OS data lives behind `IPlatform`, and an OS branch uses `OperatingSystem.Is*()` or the injected platform's `Os`, never `#if` or `RuntimeInformation`.
- Before writing code, open the standards in `.agents/coding-standards/` that apply to the files you are changing (Claude Code gets an index of them by area). Do not read all of them.
- A standard holds the full rule. When a standard and this file or a guide in `docs/` disagree, the standard is right: fix the other file in the same change.
- Code that breaks a standard is fixed, not copied. Each known violation has an issue in the `Code Cleanup` milestone.

## Decisions and planned work

- The repository's tracked work lives in issues on the tracker, never in a document. Nobody picks up work from a spec, plan, or note in the repository or in `.han/out/`. When a plan is ready, turn it into issues; the issue says what to build.
- Every issue carries labels from the repository's Forgejo label set (the Advanced set, scoped as `Scope/Name`):
  - at least one `Kind/` label (Bug, Feature, Enhancement, Documentation, Security, Testing);
  - exactly one `Priority/` label (Critical, High, Medium, Low);
  - a `Status/`, `Reviewed/`, or `Compat/Breaking` label when it applies.

  Use only labels that exist in the repository; do not create new ones. Add a milestone only when the issue belongs to one.
- Han skills write their documents to `.han/out/`, which git ignores. Treat them as working notes for the session that made them. Never put a spec, plan, standard, or other agent material in `docs/`, which holds only the published guides.
- A design call, or a change that departs from the spec, gets the next numbered entry in `docs/decisions.md` (`## D<n>. <title>`, the decision, then the reason). Do not create one file per decision (`docs/adr/`), and do not use the ADR skill here. Code comments and docs cite decisions by number, so the numbers never change.
- New coding standards come from the `han-coding:coding-standard` skill, which writes them to `.agents/coding-standards/` and indexes them in `.claude/rules/coding-standards/`. Do not list them in this file.

## Changing files

- Edit files only with an editor or the agent harness's Edit tool (Write only for a new file or one just read in full). Never use sed, awk, perl, python, PowerShell, or a shell redirect.
- Never redirect to `nul`. On Windows it can create a file named `nul` that blocks packaging.

## Branches and commits

- Work on a branch named `issue-<n>-<slug>` and open a pull request to `main`.
- The maintainer signs every commit on `main`. Contributed commits do not need to be signed.
- Write commit messages and PR descriptions in ASCII. Say what changed and why. Add no tool attribution footer.
- Make follow-ups found along the way into issues, not comments in code or notes in docs.

## Coordinating several agents on one machine

These rules apply when one session (the coordinator) dispatches implementer agents on a single workstation, for example to work through several issues at once.

- The coordinator keeps one branch for the whole batch, and the batch reaches `main` through that branch's single pull request.
- Implementers work in isolated worktrees on branches of their own. When an implementer finishes, the coordinator merges its branch into the batch branch, then removes the implementer's worktree and branch.
- The coordinator decides how many implementers run side by side, from the work itself and the resources it touches (files, test fixtures, CPU and memory). There is no fixed cap.
- Implementers never run the full gate. Each runs only the test classes for the files it changed, one class at a time, in the foreground, with `--max-threads 2` and a 5-minute timeout, and never two test processes at once.
- The full gate runs once per batch: when every implementer has finished and its work is merged, the coordinator runs `build/gate.ps1` on the batch branch, then opens the pull request. It never runs the gate on an implementer's branch.
- Every Windows gate takes one machine-wide lock (`<main checkout>/.tools/locks/gate.lock`) and runs at below-normal priority with capped parallelism, so Windows gates never overlap. The Linux gate runs on the Linux VM under its own lock there (D60), so one Windows gate and one VM gate can run at the same time.
- Before a gate on the main checkout, make sure that no SavePoint is running from `src/Savepoint.App/bin`. A build overwrites it.
- Stop every background shell or process you started before you report.
