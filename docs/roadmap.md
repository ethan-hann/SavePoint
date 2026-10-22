# SavePoint v2 - roadmap

Behavior spec: `../Savepoint/plan.md` plus `../Savepoint/docs/` (phase docs, proposals, `ui/` visual spec). Departures and gap-fills: `docs/decisions.md`. Inventories of the v1 code: `docs/reference/`.

Status keys: `todo`, `doing`, `done`. A milestone is done when its done-criteria pass on `main` with the full local gate green (`build/gate.ps1`: build, tests, format check).

## Working rules

- Tests first for every service-layer behavior. A red must be an assertion failure for the expected reason, not a compile or harness error. A test that passes on first run gets a mutation check before it counts.
- No user-visible string outside the catalog (`en.json`). The catalog never contains "commit", "branch", "checkout", "repo", or "HEAD" (test-enforced).
- Inline code comments are short and say why, never which milestone or slice produced the code. Public APIs carry XML doc comments stating their contract (D24); CS1591 is an error in Core.
- Tests never touch real user folders: every test uses a fresh temp directory and injected app-data/storage paths.
- Machine load (owner's request, 2026-10-21): at most two implementer agents at a time, and builds/tests never overlap - every gate takes one machine-wide lock (`<main checkout>/.tools/locks/gate.lock`) and runs at below-normal priority with capped parallelism (4 MSBuild nodes, no lingering nodes, one test module at a time, 4 test threads). Agents never run the full gate: they run one test class at a time in the foreground with `--max-threads 2` and a 5-minute timeout, never in the background, never two test processes at once, no repeated runs. Only the orchestrator runs the full Windows and Linux gates, on `main`, at merge points, one at a time. The orchestrator checks the machine (its own leftover processes, agents' test processes) at every check-in and kills what its agents left behind. (Second owner complaint, 2026-10-21 11:40: the PC became unusable while two agents kept re-running full gates.) Read-only reviewers may run alongside one implementer.
- Agents stop every background shell or process they started before they report (a finished agent's leftover task once ran for 10 hours and held a worktree folder open); the orchestrator checks for leftovers when removing a worktree.
- Implementers run in isolated worktrees; the orchestrator merges into `main`, runs both gates, then removes the worktree and its branch right away (`git worktree remove --force`, falling back to `rm -rf` + `git worktree prune` when Windows file locks refuse).

## Milestones

Cross-cutting: the Linux gate (`build/gate-linux.sh` under WSL, from its own worktree inside the workspace) runs after every merge once the bundled git lands; macOS stays unverified on hardware (D9).

### M0 - Scaffold (done)
Solution, projects, central package versions, `.editorconfig`, `build/fetch-mingit.ps1` (pinned version + SHA-256), bundled-git resolution and startup verification, Avalonia window showing one catalog-sourced string in the MD3 theme, test projects wired, `build/gate.ps1`.
Done when: `dotnet build` and `dotnet test` pass, the app launches a themed window, and a test proves the bundled git runs and reports a version with no system git on PATH.

### M1 - Engine (done; independent review findings fixed, D29; the fixes are re-checked by the M7 final review)
Path resolver (`{documents}`, `{appData}`, `{localAppData}`, `{userHome}`), path safety (two-tier denylist, real-path resolution, consent markers), git process runner (argument lists, streaming, stdin, binary stdout, cancellation with tree kill), git engine (repo-pair init with split git-dir/work-tree, capture with progress, checkout with lock retry/backoff, dirty detection, diff stat, discard, history, detached restore of an older save, branch fork/delete, reset, repack, object counts, blob read at ref), free-space preflight, stale `index.lock` recovery, git config tuning.
Also: `info/exclude` for ignore rules and the marker (D13.1), `info/attributes` `-delta` list (D16), capture-id trailers (D15), fork and move-forward from a detached point (D14), points-only-in-this-branch query (D17), every gotcha G-1..G-37 in `docs/reference/v1-engine.md` fixed by design.
Done when: the plan's Phase 1 verification passes (round trip byte-identical, locked file and low disk fail safely, denylisted paths rejected, nested path accepted, missing/foreign marker refuses every destructive op), a tree deeper than 260 characters round-trips on Windows (D19), and an independent Opus review (han-coding:code-review) of path safety and the engine reports no open blocking finding.

### M2 - Catalog and game services (done; review fixes D31, recheck fixes D37)
Models, game-definition validation, atomic JSON catalog, templates, game CRUD with consent confirmation and cross-game overlap checks, orphaned-marker reconnect, profile CRUD, duplicate profile, delete game/profile cleanup, storage usage, relocate storage, compaction (manual + automatic threshold), clear all data.
Also: delete-profile warning for history only it holds (D17), running-game guards (D13.5), startup stale-lock recovery (D18).
Done when: the plan's Phase 2 criteria pass (define a game, capture a profile, survive restart, denylisted/claimed paths refused with specific reasons).

### M3 - Launch orchestration and jobs (done; review fixes D31)
Job registry with history and cancellation, per-game lock, launch preview, checkout, exe and URI launch (Windows exe, Linux binary/script, macOS .app, URIs per OS), exit detection (child exit, process-name poll, manual finish), post-play capture (new point, update existing, fork new profile, move forward, not now - D14), keep-as-new-profile rescue, go back to any history point (D15), exe version reader, executable scanner, notification interface.
Done when: a scripted run with FakeGame creates a game, captures a profile, launches it, detects exit, and offers capture, with specific errors on injected failures (locked file, missing exe).

### M4 - UI foundation (done)
MD3 palette from seed (five presets, light/dark, match-OS), theme tokens and control styles, Roboto/Roboto Mono/Material Symbols fonts, catalog loader + ICU formatting + XAML markup extension, catalog lint tests, app shell (sidebar and library layouts, toggle), job status indicator, toast host, shared error card, dialogs host.
Done when: headless render tests produce PNGs of the shell in both layouts and both themes that match the mockup, and the catalog lint tests pass (banned git words, every referenced key exists, no unreferenced keys, extra locales match English - D20).

### M5 - Screens (done; UX and accessibility fixes D35, D36)
First run (with the anti-cheat note, D19), Games (header, profile table, storage footer, overflow menu), Game editor (with cloud-save guidance, D19), New profile wizard, Launch flow (banner, dialog, progress, running, post-play incl. D14 choices), History (list and cards, go back to any point, D15), Settings (storage + move + optimize + clear all data, appearance, when you finish playing), error states, startup error screens (git unavailable, corrupt catalog - D18).
Done when: every mockup screen (`2a`-`2l`, `3a`-`3c`) has a view-model test suite and a headless render reviewed against its screenshot.

### M5a - Game artwork (done, D30; merged `4a2e306`)
Owner request: Steam artwork (local Steam cache first, then Steam's CDN when "Look up game artwork online" is on), the name tint as fallback, and the user's own image always wins. Optional `SteamAppId` on the game (stored wins, otherwise detected from the launch link, `steam_appid.txt`, or the Steam library manifest), `IArtworkApi`, images under `<config>/artwork/<gameId>/`, the editor's Artwork section, the Settings toggle, and `ArtTile` drawing the image with the tint while loading.
Done when: a Steam game's cards and tiles show its art from the local cache with online lookup off; a game with no id keeps the tint; the user's image beats both and survives restart; delete game and clear all data remove the files; no test reaches the network; headless renders of the library and sidebar layouts with art are reviewed. Follow-up, not v1: GOG artwork from `goggame-<id>.info`.

### M6 - Hardening (doing: accessibility review and fixes, logging and crash handler (D34), responsive rules and reduced motion done; manual screen-reader pass and macOS checks remain)
Keyboard flow and automation names on every control, live-region announcements, responsive rules (rail collapses below ~900px, table drops mod-files column first), reduced motion, OS notifications when unfocused, single instance, logging with "Show details", crash/stale-state recovery on startup.
Done when: an accessibility review and an adversarial resilience review report no open blocking findings.

### M7 - Packaging and end-to-end (doing: packages `0c55208`, the installer data-folder guard D38, end-to-end journeys `3f20075`, and the final gap analysis D39 are done; the polish pass, final code review, and package rebuild remain)
Installer + portable build per OS with the pruned, licensed git bundle (D21), end-to-end dogfood run against FakeGame through the real UI services, final code review and gap analysis against `plan.md` and `docs/ui/README.md`, project documentation (README, user guide, developer guide) and a generated API reference (D24).
Done when: the gap analysis reports no unmet requirement and the packaged app runs the full capture, switch, play, capture cycle.

## Follow-ups

There is no remote and no issue tracker, so open follow-ups live here.

- Test teardown race: `GitWorkspace.DeleteDirectory` lists files and then calls `SetAttributes`, and a temp pack file git removes in between throws `FileNotFoundException` (seen once in `ProfilesManageTests.Deleting_a_profile_that_is_not_checked_out_removes_its_branches_and_row`). The teardown should tolerate a file that is already gone.
- Fixture rework, deferred (`.tools/briefs/test-fixtures.md`): the slowest classes are `CheckoutTests`, `LaunchContinueTests`, `OrphanTests`, and `HistoryInputTests`.
- GOG artwork from `goggame-<id>.info` (D30 follow-up).
- Test isolation: neither `MainWindowViewModel` nor `ToastHostViewModel` can be disposed, so a finished journey's toast timers still post to the UI thread inside later tests (12 such posts, up to 6 s late, in one traced run). It does no harm today; making both disposable ends it.
- Avalonia's `WriteableBitmapImpl.Save` encodes from the bitmap's pixels without keeping the bitmap alive, so a collection mid-encode frees the pixels. The tests hold every frame through `ThemeTestSupport.SaveFrame`; the gap could be reported upstream.
- Packaging follow-ups:
  - code signing, so SmartScreen does not warn;
  - publish the MSYS2 and MINGW package sources D21 promises;
  - use the `.ico` for the Windows title bar instead of the downscaled 256 PNG;
  - consider the filled bookmark glyph for icon sizes up to 32 px, where the outline is soft;
  - the in-app mark centers the glyph by its em box, so it sits about 2 px low against the icon.
- Release packages are build outputs: rebuild them with `build/package.ps1` and `build/package.sh` from `main` for a release.
- Backend review notes not covered by D31: none; the recheck is done, and its calls are D37.
- Storage follow-ups from the recheck:
  - after **Start with an empty folder**, a storage move is refused until some game saves;
  - a left-alone folder skipped by a new capture or an update reports `FolderOutcome.Ok` instead of saying it was skipped;
  - a new capture over a file another program holds still fails at `git add` instead of naming the held file.
