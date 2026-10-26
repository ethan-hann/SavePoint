# Coding standards

How code is written in SavePoint where a tool cannot decide it. Each standard is one file in this folder. A code review checks a change against the standards that apply to it.

This folder holds instructions for people and agents changing the code. It is not product documentation: `docs/` holds the user and developer guides that the documentation site publishes, and nothing here is built into it.

The binding rules in `AGENTS.md` come first, and most of them already have a check: the catalog lint tests (copy and vocabulary), CS1591 in Core (doc comments), `dotnet format` (layout), and the gate. A standard here adds the judgment calls those checks cannot make. It never restates or loosens an `AGENTS.md` rule.

## Adding a standard

Run the `han-coding:coding-standard` skill. It writes the standard here, with a `paths:` list of the files it governs, and lists it in a per-file-type index under `.claude/rules/coding-standards/` (for example `csharp.md` for `**/*.cs`). Claude Code loads that index when it reads a matching file and opens only the standards that fit the task. Other agents read `AGENTS.md`, which points here.

Do not list standards in `AGENTS.md` or `CLAUDE.md`. The index files are how they are found.

A standard starts with the status `proposed` and becomes `accepted` once the maintainer approves it.

## The standards

None yet.
