@AGENTS.md

## Claude Code notes

The rules are in `AGENTS.md`, imported above. That includes its `## Project Discovery` section, which Han skills read for this project's docs, decisions, standards, and commands.

- Coding standards reach Claude Code by area (`core.md`, `app.md`, `tests.md`, `tools.md`) through the index files in `.claude/rules/coding-standards/`. When a file you read matches an index's `paths:`, the index loads and you open only the standards that fit the task.
- Han is a per-machine plugin, not part of this repository. The project's Han settings are in `.han/config.md`.
