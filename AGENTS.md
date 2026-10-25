# Working on SavePoint

Rules for anyone changing this repository, human or agent. The product docs live in `docs/`: start with `docs/developer-guide.md` (build, test, package), then `docs/architecture.md`, `docs/decisions.md` (every design call, D1 onward), and `docs/ui-guide.md` for screens. Planned work is tracked as issues and milestones on https://git.tortal.tech/ethan-hann/SavePoint.

SavePoint is licensed GPL-3.0-or-later (`LICENSE`). Bundled components keep their own licenses (`THIRD-PARTY-NOTICES.md`, `licenses/`); a new dependency needs a license compatible with GPL-3.0 and an entry in the notices.

## Gates

- `build/gate.ps1` (Windows) and `build/gate.sh` (Linux, macOS) build with warnings as errors, run every test, and check formatting. A change is done when the gate passes.
- CI runs the Linux gate and the Windows gate on every push to `main` and every pull request (`.forgejo/workflows/gate.yml`); the Windows job runs on a self-hosted Windows VM. A change that touches only documentation (`docs/`, the documentation site's `docfx.json` and `docfx/`, and Markdown files at the root, except `THIRD-PARTY-NOTICES.md`) passes without building or testing, and needs no local gate either; `.forgejo/workflows/docs.yml` builds the site for it instead. When the Windows job cannot run (the runner is offline), run `build/gate.ps1` locally before merging anything that touches Windows code or the UI.
- `SAVEPOINT_TEST_ARGS` takes only a report (`--report-*`) and retries (`--retry-failed-tests*`). A filter is refused unless `-AllowNarrowedTests` / `--allow-narrowed-tests` says it is on purpose, and a narrowed run is not a gate.

## Code

- Tests first for every behavior. A red must be an assertion failure for the expected reason, not a compile or harness error. A test that passes on its first run gets a mutation check before it counts. Record the observed red in the commit message.
- Every user-visible string goes through the catalog (`src/Savepoint.App/Localization/Assets/Lang/en/*.json`); keys are literal. The catalog never says "commit", "branch", "checkout", "repo", or "HEAD", uses no em dashes, and states permanence in the title or the button (D44). Lint tests enforce this.
- Players may not know version control. Use the D44 vocabulary (snapshot, profile, Go back, Recently removed, Free up space) and explain a new concept in plain words, with a question button (such as "What's a snapshot?") when it needs one.
- Inline comments are short and say why. They never name a milestone, slice, issue, or build step. Public APIs carry XML doc comments stating their contract; CS1591 is an error in Core.
- Tests never touch real user folders: every test uses a fresh temp directory and injected app-data and storage paths.
- Cross-platform by default: Windows, Linux, and macOS run from one target framework, and OS-specific code is guarded with `OperatingSystem.Is*()` (D9, D26).

## Changing files

- Edit files only with an editor or the agent harness's Edit tool (Write only for a new file or one just read in full). Never sed, awk, perl, python, PowerShell, or a shell redirect: they fail silently, report false successes, and can change other files.
- Never redirect to `nul`; on Windows it can create a file named `nul` that blocks packaging.

## Branches and commits

- Work on a branch named `issue-<n>-<slug>` and open a pull request to `main`. The maintainer signs every commit on `main`; contributed commits do not need to be signed.
- Commit messages and PR descriptions are ASCII, say what changed and why, and carry no tool attribution footer.
- Follow-ups found along the way become issues, not comments in code or notes in docs.

## Coordinating several agents on one machine

These apply when one session dispatches implementer agents on a single workstation, so the machine stays usable while they work.

- At most two implementer agents at a time. Implementers work in isolated worktrees; remove each worktree and its branch right after it merges.
- Builds and test runs never overlap: every gate takes one machine-wide lock (`<main checkout>/.tools/locks/gate.lock`) and runs at below-normal priority with capped parallelism.
- Agents never run the full gate. They run one test class at a time, in the foreground, with `--max-threads 2` and a 5-minute timeout, never two test processes at once. Only the coordinating session runs the full Windows and Linux gates, on `main`, one at a time.
- Before a gate on the main checkout, check that no SavePoint is running from `src/Savepoint.App/bin`, since a build would overwrite it.
- Stop every background shell or process you started before you report.
