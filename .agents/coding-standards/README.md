# Coding standards

How code is written in SavePoint where a tool cannot decide it. Each standard is one file in this folder. A code review checks a change against the standards that apply to it.

## The standards

| Standard | Read when |
| --- | --- |
| [Api results and errors](core-api-results-and-errors.md) | Adding or changing an Api member, an error type, or App code that shows a failure. |
| [Injected side effects in Core](core-injected-side-effects.md) | Core code needs the time, the environment, a process, disk space, or the network, or adds a static field. |
| [Start processes with an argument list](csharp-process-argument-list.md) | Starting git, a game, a file manager, or any other process. |
| [Platform-specific code](csharp-platform-specific-code.md) | Code or a test behaves differently on Windows, Linux, or macOS. |
| [View models, views, and screen wiring](app-mvvm-view-models.md) | Adding or changing a screen, dialog, view model, or view. |
| [Test names and structure](tests-naming-and-structure.md) | Adding a test, a test helper, or a wait in a test. |
| [Tests that can fail and run fast](tests-meaningful-and-fast.md) | Adding or changing a test, a theory, a fake, or a rig, or a test that builds git state or opens a window. |
| [Comments that say why](csharp-comments.md) | Writing or changing a comment or a doc comment. |

Code that breaks a standard is fixed, not copied. A standard that still has violations lists them under "Known violations", and each has an issue in the `Code Cleanup` milestone on the tracker; a standard with none has no such list.

## How the standards relate to the other rules

This folder holds instructions for people and agents changing the code. It is not product documentation: `docs/` holds the user and developer guides that the documentation site publishes, and nothing here is built into it.

`AGENTS.md` holds the short form of the rules every change follows, and most of them have a check: the catalog lint tests (copy and vocabulary), CS1591 in Core (doc comments), `dotnet format` (layout), and the gate. A standard here holds the full rule and the judgment calls those checks cannot make. When a standard and `AGENTS.md` or a guide in `docs/` disagree, the standard is right, and the other file is fixed to match.

## Adding a standard

Run the `han-coding:coding-standard` skill. It writes the standard here, with a `paths:` list of the files it governs, and lists it in an index under `.claude/rules/coding-standards/`. The indexes are grouped by area: `core.md` for `src/Savepoint.Core`, `app.md` for `src/Savepoint.App`, `tests.md` for `tests/`, and `tools.md` for `tools/`. Claude Code loads an index when it reads a matching file and opens only the standards that fit the task. Other agents read `AGENTS.md`, which points here.

Do not list standards in `AGENTS.md` or `CLAUDE.md`. The index files are how they are found.

A standard starts with the status `proposed` and becomes `accepted` once the maintainer approves it.
