# SavePoint v2 - roadmap

Behavior spec: `../Savepoint/plan.md` plus `../Savepoint/docs/` (phase docs, proposals, `ui/` visual spec). Departures and gap-fills: `docs/decisions.md`. Inventories of the v1 code: `docs/reference/`.

Status keys: `todo`, `doing`, `done`. A milestone is done when its done-criteria pass on `main` with the full local gate green (`build/gate.ps1`: build, tests, format check).

## Working rules

- Tests first for every service-layer behavior. A red must be an assertion failure for the expected reason, not a compile or harness error. A test that passes on first run gets a mutation check before it counts.
- No user-visible string outside the catalog (`en.json`). The catalog never contains "commit", "branch", "checkout", "repo", or "HEAD" (test-enforced).
- Code comments are short and say why, never which milestone or slice produced the code.
- Tests never touch real user folders: every test uses a fresh temp directory and injected app-data/storage paths.
- Implementers run in isolated worktrees; the orchestrator merges into `main`.

## Milestones

### M0 - Scaffold (done, except bundled git -> W1)
Solution, projects, central package versions, `.editorconfig`, `build/fetch-mingit.ps1` (pinned version + SHA-256), bundled-git resolution and startup verification, Avalonia window showing one catalog-sourced string in the MD3 theme, test projects wired, `build/gate.ps1`.
Done when: `dotnet build` and `dotnet test` pass, the app launches a themed window, and a test proves the bundled git runs and reports a version with no system git on PATH.

### M1 - Engine (doing)
Path resolver (`{documents}`, `{appData}`, `{localAppData}`, `{userHome}`), path safety (two-tier denylist, real-path resolution, consent markers), git process runner (argument lists, streaming, stdin, binary stdout, cancellation with tree kill), git engine (repo-pair init with split git-dir/work-tree, capture with progress, checkout with lock retry/backoff, dirty detection, diff stat, discard, history, detached restore of an older save, branch fork/delete, reset, repack, object counts, blob read at ref), free-space preflight, stale `index.lock` recovery, git config tuning.
Done when: the plan's Phase 1 verification passes (round trip byte-identical, locked file and low disk fail safely, denylisted paths rejected, nested path accepted, missing/foreign marker refuses every destructive op).

### M2 - Catalog and game services (doing: catalog store)
Models, game-definition validation, atomic JSON catalog, templates, game CRUD with consent confirmation and cross-game overlap checks, orphaned-marker reconnect, profile CRUD, duplicate profile, delete game/profile cleanup, storage usage, relocate storage, compaction (manual + automatic threshold), clear all data.
Done when: the plan's Phase 2 criteria pass (define a game, capture a profile, survive restart, denylisted/claimed paths refused with specific reasons).

### M3 - Launch orchestration and jobs (todo)
Job registry with history and cancellation, per-game lock, launch preview, checkout, exe and URI launch, exit detection (child exit, process-name poll, manual finish), post-play capture (new point, update existing, not now), keep-as-new-profile rescue, older-save restore, exe version reader, executable scanner, notification interface.
Done when: a scripted run with FakeGame creates a game, captures a profile, launches it, detects exit, and offers capture, with specific errors on injected failures (locked file, missing exe).

### M4 - UI foundation (doing: color port, fonts)
MD3 palette from seed (five presets, light/dark, match-OS), theme tokens and control styles, Roboto/Roboto Mono/Material Symbols fonts, catalog loader + ICU formatting + XAML markup extension, catalog lint tests, app shell (sidebar and library layouts, toggle), job status indicator, toast host, shared error card, dialogs host.
Done when: headless render tests produce PNGs of the shell in both layouts and both themes that match the mockup, and the catalog lint tests pass.

### M5 - Screens (todo)
First run, Games (header, profile table, storage footer, overflow menu), Game editor, New profile wizard, Launch flow (banner, dialog, progress, running, post-play), History (list and cards), Settings (storage + optimize + clear all data, appearance, when you finish playing), error states.
Done when: every mockup screen (`2a`-`2l`, `3a`-`3c`) has a view-model test suite and a headless render reviewed against its screenshot.

### M6 - Hardening (todo)
Keyboard flow and automation names on every control, live-region announcements, responsive rules (rail collapses below ~900px, table drops mod-files column first), reduced motion, OS notifications when unfocused, single instance, logging with "Show details", crash/stale-state recovery on startup.
Done when: an accessibility review and an adversarial resilience review report no open blocking findings.

### M7 - Packaging and end-to-end (todo)
Installer + portable build with bundled MinGit, end-to-end dogfood run against FakeGame through the real UI services, final code review and gap analysis against `plan.md` and `docs/ui/README.md`.
Done when: the gap analysis reports no unmet requirement and the packaged app runs the full capture, switch, play, capture cycle.
