---
paths:
  - "tests/**/*.cs"
---

# Tests coding standards index

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

- [Comments that say why](../../../.agents/coding-standards/csharp-comments.md) - What a comment says and what it never
  names. Read when writing or changing a comment in a test.
- [Platform-specific code](../../../.agents/coding-standards/csharp-platform-specific-code.md) - How a test skips on an
  OS it cannot run on, and the expected-skip lists the gate checks. Read when a test cannot pass on Windows, Linux, or
  macOS.
- [Start processes with an argument list](../../../.agents/coding-standards/csharp-process-argument-list.md) - Every
  process starts with `ArgumentList`, and `cmd.exe` built-ins such as `mklink` get one list item per argument. Read when
  a test or test helper starts a process.
- [Test names and structure](../../../.agents/coding-standards/tests-naming-and-structure.md) - Test names as behavior
  sentences, temp folders, shared repository templates, waits, and the fake and contract backends. Read when adding a
  test, a test helper, or a wait.
