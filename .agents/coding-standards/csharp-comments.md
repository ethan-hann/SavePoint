---
paths:
  - "src/**/*.cs"
  - "tests/**/*.cs"
  - "tools/**/*.cs"
---

# Comments that say why

- **Status:** accepted
- **Date Created:** 2026-10-26 18:00
- **Last Updated:** 2026-10-26 18:10
- **Applies To:**
  - Any comment or XML doc comment in C# under `src/`, `tests/`, or `tools/`.

## Introduction

A comment in SavePoint says why the code is the way it is: the fact about the outside world, the decision, or the failure it guards against. It never restates what the code does, and it never names the work that produced it. A public Core API carries an XML doc comment that states its contract.

### Purpose

- **Primary:** A comment stays true as long as the code it sits on. A reason tied to the code or to a numbered decision does not go stale when the project's plans, issues, or reviews move on.
- **Secondary:** A reader learns the one thing the code cannot tell them: why it is not done the obvious way.
- **Side effect:** Comments stay short, because a reason fits in a line or two.

### Scope

C# comments (`//`) and XML doc comments (`///`) in `src/`, `tests/`, and `tools/`. The words of user-visible copy are covered by the catalog lint tests, not by this standard.

## When to Apply

1. **Would a careful reader ask why the code does this?** For example, a check that looks redundant, an order that looks arbitrary, or a value with no obvious source.
   - If yes, write a comment that answers the question (see "Write the reason, not the action").
   - If no, write no comment.
2. **Does a numbered decision in `docs/decisions.md` explain the reason?**
   - If yes, cite it as `D<n>` (see "Cite decisions by number").
   - If no, state the reason in the comment.
3. **Is the member public API in `Savepoint.Core`?**
   - If yes, give it an XML doc comment that states its contract (see "State the contract of public Core APIs").
   - If no, add a doc comment only where the contract is not obvious from the name.

**Verification step:** search comments for words that name work instead of code:

```bash
grep -rnE --include=*.cs "//.*(\b(TODO|FIXME|HACK|milestone|slice|review|task|build step|pull request|PR|W-[0-9]+)\b|#[0-9]+)" src tests tools
```

Read each hit. A word used in its plain sense is fine: "progress milestones", "a 15-character slice", "a task it then waits on", and the launch flow's review of a half-switched folder, which is a screen in the product. A hit that names a piece of work is a violation.

## When NOT to Apply

- **Code that explains itself.** A well-named method and variable need no comment. Do not add one to reach a quota.
- **A test name.** A test's name states its behavior (see [Test names and structure](tests-naming-and-structure.md)). Comment a test only for a reason the name cannot hold, such as why a step is compared one at a time.

## Background

`AGENTS.md` (section "Code") and `docs/developer-guide.md` (section "Rules for changes") hold the short form of these rules. D24 made a missing XML doc comment on a public Core API a build error. `Directory.Build.props` turns CS1591 off for every project, and `Savepoint.Core.csproj` turns it back on for Core. D39 keeps it off in the App, where public surface is documented by hand.

## Coding Standard

### Write the reason, not the action

The comment answers why. It names the outside fact, the risk, or the rule that forces the code's shape.

**Correct usage:**

```csharp
// The kernel cut the name as the game wrote it, so also try it with the .exe the user may have left off; a 15-character slice only equals a full-length comm.
foreach (var form in new[] { configured, wanted, wanted + ".exe" })
```

**What to avoid:**

```csharp
// Loop over the three forms and compare each one.
foreach (var form in new[] { configured, wanted, wanted + ".exe" })
```

**Examples:**

- `src/Savepoint.Core/Platform/ProcessNameMatcher.cs`, `ProcessNameMatcher.IsTruncatedComm`: the comment gives the kernel rule that the loop handles.

### Cite decisions by number

When a decision in `docs/decisions.md` is the reason, cite it as `D<n>` (or `D<n>.<m>` for one point in it), inline in the sentence or in parentheses. Do not restate the whole decision.

**Correct usage:**

```csharp
/// Decides whether the main window may close while jobs run (D31). A clear-all or a storage move that is cut short leaves a
/// half-done job behind, ...
```

**What to avoid:**

```csharp
// See the decision about closing the window while jobs run.
```

**Examples:**

- `src/Savepoint.App/Features/Shell/ExitGuard.cs`, `ExitGuard`: the summary cites D31 and gives the consequence.

### Never name the work that produced the code

A comment never names a milestone, slice, issue number, task, review, pull request, or build step. Those are gone when the work is done, and the comment then points at nothing. Say what the code is for instead. Follow-up work goes in an issue, not in a `TODO`.

**Correct usage:**

```csharp
// Pins three theme facts that are easy to lose: the warning family is gold, the accent swatches need the raw seeds, and a recessed
// well shows in light mode.
public class ThemePolishTests
```

**What to avoid:**

```csharp
// Findings from the theme review: the warning family is gold, the accent swatches need the raw seeds, and a recessed well shows in light mode.
public class ThemePolishTests
```

**Known violations:**

- `tests/Savepoint.App.Tests/Theme/ThemePolishTests.cs`, `ThemePolishTests`: names a review.
- The render tests, such as `GamesRenderTests` and `HistoryRenderTests`: their headers say the images are "for review". Say what a person compares them with instead.

### State the contract of public Core APIs

A public type or member in `Savepoint.Core` has an XML doc comment that states its contract: what it returns, which error ids it fails with, and what a parameter means when that is not obvious. The build fails without one (D24, CS1591).

**Correct usage:**

```csharp
/// <summary>Lists every game in catalog order. Fails with <c>error.catalog.corrupt</c> when the games file is unreadable.</summary>
/// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
```

**What to avoid:**

```csharp
/// <summary>Lists the games.</summary>
```

**Examples:**

- `src/Savepoint.Core/Api/IGamesApi.cs`, `IGamesApi.ListAsync`: the contract, its error id, and a decision cite.
- `src/Savepoint.Core/Savepoint.Core.csproj`: the `NoWarn` line that turns CS1591 back on for Core.

## Additional Resources

### Project Documentation

- `AGENTS.md`, section "Code".
- `docs/developer-guide.md`, section "Rules for changes".
- `docs/decisions.md`, D24 (doc comments) and D39 (the App's public surface).
- [Api results and errors](core-api-results-and-errors.md): the error ids a doc comment names.
