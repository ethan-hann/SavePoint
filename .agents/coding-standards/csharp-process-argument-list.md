---
paths:
  - "src/**/*.cs"
  - "tests/**/*.cs"
  - "tools/**/*.cs"
---

# Start processes with an argument list

- **Status:** accepted
- **Date Created:** 2026-10-26 17:40
- **Last Updated:** 2026-10-26 18:10
- **Applies To:**
  - Any C# code that builds a `ProcessStartInfo` or calls `Process.Start`, in the app, its tests, or the repository's tools.

## Introduction

SavePoint starts every process with `ProcessStartInfo.ArgumentList`. Each argument is one list item, and nothing joins them into a command line. This covers git, game launches, the file manager, notifications, test helpers, and the tools under `tools/`.

### Purpose

- **Primary:** A path or a name from the player reaches the process exactly as written. Game folders, profile names, and file names hold spaces and characters that a command-line parser reads as syntax. As a list item, .NET quotes each one for the program, so it cannot split into two arguments or change the command.
- **Secondary:** No code has to quote or escape an argument, and so no code can get the quoting wrong on one OS.
- **Side effect:** A test can compare the arguments as a list instead of parsing a string.

### Scope

C# under `src/`, `tests/`, and `tools/`.

## When to Apply

1. **Does the code start a process?** Look for `new ProcessStartInfo`, `Process.Start`, or a type that wraps one (`ProcessStartRequest`, `ProcessCommand`, `GitInvocation`).
   - If yes, continue.
   - If no, this standard does not apply.
2. **Does the process open a link through the Windows shell?** On Windows, a storefront link such as `steam://` has no opener program to call.
   - If yes, see "Exception - shell links on Windows".
   - If no, continue.
3. **Does the code need a command built into `cmd.exe`, such as `mklink`?**
   - If yes, see "Exception - `cmd.exe` built-ins in test helpers".
   - If no, apply every rule below.

**Exception - shell links on Windows:** `WindowsGameLauncher.OpenUriAsync` starts the link with `UseShellExecute = true`, because the Windows shell routes a link to the program that registered its scheme. The link is the only argument. Linux and macOS open links through `xdg-open` and `open` with an argument list.

**Exception - `cmd.exe` built-ins in test helpers:** `cmd.exe` reads its whole command line again after .NET quotes it, so a character such as `&` in a path can still act as syntax. Start `cmd.exe /c` only from a test helper, only with paths the test created, and still with one list item per argument. Production code never starts `cmd.exe`.

**Verification step:** search for a joined argument string:

```bash
grep -rnE --include=*.cs 'ProcessStartInfo\([^)]*\) *\{.*\bArguments *=|new ProcessStartInfo\("[^"]+", *\$?"' src tools tests
```

The search prints nothing after the known violations below are fixed.

## When NOT to Apply

- **A script file run as a program.** Starting `/bin/sh` with the script's path and its arguments as list items, as `UnixLauncherCore` does for a `.sh` game, follows this standard. The rule bans a command string, not a shell program.
- **A test that checks how the runner treats a shell.** `SystemProcessRunnerTests` runs `cmd /c` or `/bin/sh -c` on purpose, still through the argument list, to test the runner itself.

## Background

`docs/architecture.md` (section "Running git") states that git runs with argument lists, never shell strings. The same reasoning holds for every process. The process wrappers `GitInvocation`, `ProcessStartRequest`, and `ProcessCommand` carry their arguments as lists, so a caller cannot pass a joined string by accident.

## Coding Standard

### Add each argument to `ArgumentList`

Build the `ProcessStartInfo` with only the program, and add each argument with `ArgumentList.Add`. Set `UseShellExecute = false`. The one exception is a Windows shell link (see "Exception - shell links on Windows").

**Correct usage:**

```csharp
var startInfo = new ProcessStartInfo(_binary.ExecutablePath)
{
    UseShellExecute = false,
    CreateNoWindow = true,
    ...
};

foreach (var argument in invocation.Arguments)
{
    startInfo.ArgumentList.Add(argument);
}
```

**What to avoid:**

```csharp
// SavePoint quotes the path by hand, a second copy of the quoting that ArgumentList already does.
return new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{path}\"", UseShellExecute = false };
```

**Correct usage (the same Explorer call as list items):**

```csharp
OsKind.Windows => new ProcessCommand(_explorerPath, isFile ? ["/select,", full] : [full]),
```

**Examples:**

- `src/Savepoint.Core/Git/GitRunner.cs`, `GitRunner.CreateStartInfo`: every git call.
- `src/Savepoint.App/Platform/Processes/SystemProcessRunner.cs`, `SystemProcessRunner.CreateStartInfo`: every App process.
- `src/Savepoint.App/Platform/FileManager.cs`, `FileManager.Reveal`: Explorer's `/select,` and the path as two list items.

**Known violations:**

- `src/Savepoint.App/Features/Settings/SettingsEnvironment.cs`, `ProcessFileRevealer.Start`: the Windows branch joins a string.

### In app code, pass arguments through the wrapper type

App and Core code that needs a process builds the wrapper for its layer, with the arguments as a collection: `GitInvocation` for git, `ProcessStartRequest` for a launch, `ProcessCommand` in the App. Only the runner for that wrapper touches `ProcessStartInfo`. Tests and tools have no wrapper and build `ProcessStartInfo` directly, with `ArgumentList`.

**Correct usage:**

```csharp
using var opener = Spawn.Opener(_starter, new ProcessStartRequest(_opener, [uri]), uri);
```

**What to avoid:**

```csharp
// A second path to Process.Start that skips the runner's logging and error handling.
Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { uri } });
```

**Examples:**

- `src/Savepoint.Core/Launch/UnixLauncherCore.cs`, `UnixLauncherCore.OpenLinkAsync`: a launch request with its arguments.
- `src/Savepoint.Core/Git/GitInvocation.cs`, `GitInvocation.Arguments`: documented as never joined into a command line.
- `src/Savepoint.App/Platform/Processes/IProcessRunner.cs`, `ProcessCommand`: the App's wrapper.

### Start `cmd.exe` built-ins with one list item per argument

A test helper that needs `mklink` starts `cmd.exe` with `/c`, `mklink`, and each path as separate list items. It never formats them into one string.

**Correct usage:**

```csharp
info.ArgumentList.Add("/c");
info.ArgumentList.Add("mklink");
info.ArgumentList.Add("/J");
info.ArgumentList.Add(link);
info.ArgumentList.Add(target);
```

**What to avoid:**

```csharp
using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
```

**Examples:**

- `tests/Savepoint.Core.Tests/Paths/TempTree.cs`, `TempTree.MakeJunction`: `mklink` through the argument list.

## Additional Resources

### Project Documentation

- `docs/architecture.md`, section "Running git".
- [Platform-specific code](csharp-platform-specific-code.md): the per-OS openers and where OS code goes.

### External Resources

- [ProcessStartInfo.ArgumentList](https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.argumentlist)
