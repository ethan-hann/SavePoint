---
paths:
  - "src/Savepoint.Core/**/*.cs"
---

# Core coding standards index

You are reading this file because Claude Code loaded it as a path-scoped rule: you just read or are about to read a file
matching one of the globs in this file's `paths:` frontmatter.

This file is an **index**, not a standard. Each entry below points to a canonical coding standard with a short
description of what it covers and when it applies.

Coding standards for this project live in their canonical documentation directory (`.agents/coding-standards/`) and
are exposed to Claude Code through per-file-type index files under `.claude/rules/coding-standards/`. The full text of a
standard is loaded only when you decide it applies and use the Read tool to open it. This keeps context lean and lets
you make a relevance decision before paying the token cost.

**Do not read every linked standard.** For the specific task you are doing right now, scan the descriptions and identify
only the standards that are clearly relevant. Then use the Read tool to open just those files. If no entry is clearly
relevant, do not open any of them.

If you are unsure whether a standard applies, do not open it. The author of the work can prompt you to load a specific
standard if needed. Loading standards that do not apply burns context and dilutes attention on the ones that do.

## Available standards

- [Api results and errors](../../../.agents/coding-standards/core-api-results-and-errors.md) - What an Api member returns,
  which calls take a cancellation token, and how a failure becomes an `error.<area>.<name>` id. Read when adding or
  changing an Api member, an Api implementation, or a `SavepointException` subclass.
- [Comments that say why](../../../.agents/coding-standards/csharp-comments.md) - What a comment says, how to cite a
  decision, and what a public Core API's doc comment states. Read when writing or changing a comment or a doc comment.
- [Injected side effects in Core](../../../.agents/coding-standards/core-injected-side-effects.md) - How Core reaches the
  clock, the environment, processes, disk space, and the network, and when a static may hold state. Read when code
  needs the time or the machine, or adds a static field.
- [Platform-specific code](../../../.agents/coding-standards/csharp-platform-specific-code.md) - Where code for Windows,
  Linux, or macOS goes and which OS check to use. Read when adding a branch, an API call, or a table that differs per
  OS.
- [Start processes with an argument list](../../../.agents/coding-standards/csharp-process-argument-list.md) - Every
  process starts with `ArgumentList`, never a joined string or a shell command. Read when starting git, a game, or any
  other process.
