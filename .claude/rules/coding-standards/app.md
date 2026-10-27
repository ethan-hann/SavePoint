---
paths:
  - "src/Savepoint.App/**/*.cs"
  - "src/Savepoint.App/**/*.axaml"
---

# App coding standards index

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

- [Api results and errors](../../../.agents/coding-standards/core-api-results-and-errors.md) - How a failed Api result
  reaches the screen through `ErrorPresentation`. Read when App code handles a failed result or a new error id needs its
  look.
- [Comments that say why](../../../.agents/coding-standards/csharp-comments.md) - What a comment says and how to cite a
  decision. Read when writing or changing a comment or a doc comment.
- [Platform-specific code](../../../.agents/coding-standards/csharp-platform-specific-code.md) - Where code for Windows,
  Linux, or macOS goes and which OS check to use. Read when adding notifications, window handling, or any other
  OS-specific App code.
- [Start processes with an argument list](../../../.agents/coding-standards/csharp-process-argument-list.md) - Every
  process starts with `ArgumentList`, never a joined string or a shell command. Read when the App opens a file manager,
  a link, or another program.
- [View models, views, and screen wiring](../../../.agents/coding-standards/app-mvvm-view-models.md) - The shape of a view
  model and its commands, the view beside it, the `FeatureComposition` factory, and the UI thread. Read when adding or
  changing a screen, a dialog, a view model property or command, or a view.
