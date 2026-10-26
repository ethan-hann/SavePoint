# Working on SavePoint

Rules for anyone changing this repository, human or agent. Product docs are in `docs/`: start with `docs/developer-guide.md` (build, test, package), then `docs/architecture.md`, `docs/decisions.md` (every design call, D1 onward), and `docs/ui-guide.md` for screens. Planned work is tracked as issues and milestones on https://git.tortal.tech/ethan-hann/SavePoint.

SavePoint is licensed GPL-3.0-or-later (`LICENSE`). Bundled components keep their own licenses (`THIRD-PARTY-NOTICES.md`, `licenses/`). A new dependency needs a license compatible with GPL-3.0 and an entry in the notices.

## Gates

- Run `build/gate.ps1` (Windows) or `build/gate.sh` (Linux, macOS). It builds with warnings as errors, runs every test, and checks formatting. A change is done when the gate passes.
- CI runs the Linux gate and the Windows gate on every push to `main` and every pull request (`.forgejo/workflows/gate.yml`). The Windows job runs on a self-hosted Windows VM.
- A change that touches only documentation passes without building or testing, and needs no local gate. Documentation means `docs/`, the documentation site's `docfx.json` and `docfx/`, and Markdown files at the root, except `THIRD-PARTY-NOTICES.md`. `.forgejo/workflows/docs.yml` builds the site for it instead.
- If the Windows job cannot run (the runner is offline), run `build/gate.ps1` locally before merging anything that touches Windows code or the UI.
- `SAVEPOINT_TEST_ARGS` takes only a report (`--report-*`) and retries (`--retry-failed-tests*`). A filter is refused unless `-AllowNarrowedTests` / `--allow-narrowed-tests` says it is on purpose. A narrowed run is not a gate.

## Code

- Write tests first for every behavior. A red must be an assertion failure for the expected reason, not a compile or harness error. A test that passes on its first run gets a mutation check before it counts. Record the observed red in the commit message.
- Send every user-visible string through the catalog (`src/Savepoint.App/Localization/Assets/Lang/en/*.json`). Keys are literal. The catalog never says "commit", "branch", "checkout", "repo", or "HEAD", uses no em dashes, and states permanence in the title or the button (D44). Lint tests enforce this.
- Use the D44 vocabulary (snapshot, profile, Go back, Recently removed, Free up space). Explain a new concept in plain words, with a question button (such as "What's a snapshot?") when it needs one.
- Keep inline comments short and say why. Never name a milestone, slice, issue, or build step in them.
- Give public APIs XML doc comments that state their contract. CS1591 is an error in Core.
- Tests never touch real user folders. Each test uses a fresh temp directory and injected app-data and storage paths.
- Stay cross-platform: Windows, Linux, and macOS run from one target framework. Guard OS-specific code with `OperatingSystem.Is*()` (D9, D26).

## Changing files

- Edit files only with an editor or the agent harness's Edit tool (Write only for a new file or one just read in full). Never use sed, awk, perl, python, PowerShell, or a shell redirect.
- Never redirect to `nul`. On Windows it can create a file named `nul` that blocks packaging.

## Branches and commits

- Work on a branch named `issue-<n>-<slug>` and open a pull request to `main`.
- The maintainer signs every commit on `main`. Contributed commits do not need to be signed.
- Write commit messages and PR descriptions in ASCII. Say what changed and why. Add no tool attribution footer.
- Make follow-ups found along the way into issues, not comments in code or notes in docs.

## Coordinating several agents on one machine

These rules apply when one session dispatches implementer agents on a single workstation.

- Run at most two implementer agents at a time. Implementers work in isolated worktrees. Remove each worktree and its branch right after it merges.
- Builds and test runs never overlap. Every gate takes one machine-wide lock (`<main checkout>/.tools/locks/gate.lock`) and runs at below-normal priority with capped parallelism.
- Agents never run the full gate. They run one test class at a time, in the foreground, with `--max-threads 2` and a 5-minute timeout, and never two test processes at once.
- Only the coordinating session runs the full Windows and Linux gates, on `main`, one at a time.
- Before a gate on the main checkout, make sure that no SavePoint is running from `src/Savepoint.App/bin`. A build overwrites it.
- Stop every background shell or process you started before you report.
