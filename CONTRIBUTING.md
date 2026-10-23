# Contributing to SavePoint

Bug reports, ideas, and pull requests are welcome.

## Reporting a bug or asking for a feature

Open an issue and pick the template that fits. For a bug, the most useful things are what you did, what you expected, what happened instead, and the log from **Settings > About > Open the log folder**. Say which OS and which game, and whether the game came from Steam, GOG, or elsewhere.

## Changing the code

1. Read [AGENTS.md](AGENTS.md). It holds the rules every change follows: tests first, every user-visible string in the catalog, plain words for players, short comments that say why.
2. Build and test as [docs/developer-guide.md](docs/developer-guide.md) describes. A change is done when the gate passes: `build/gate.ps1` on Windows, `build/gate.sh` on Linux and macOS.
3. Work on a branch named `issue-<n>-<slug>` and open a pull request to `main`. CI runs the Linux gate on every pull request. There is no Windows runner, so run `build/gate.ps1` yourself if the change touches Windows code or the UI.
4. Design calls go in [docs/decisions.md](docs/decisions.md) as a new numbered entry, so the reason survives the change.

## License of contributions

SavePoint is licensed GPL-3.0-or-later. By opening a pull request, you agree that your contribution is licensed the same way. A new dependency needs a license compatible with GPL-3.0 and an entry in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
