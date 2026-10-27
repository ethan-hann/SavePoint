---
paths:
  - "src/**/*.cs"
  - "tests/**/*.cs"
---

# Platform-specific code

- **Status:** accepted
- **Date Created:** 2026-10-26 17:45
- **Last Updated:** 2026-10-26 18:10
- **Applies To:**
  - Any C# code whose behavior or values differ between Windows, Linux, and macOS.
  - Any test that cannot run on one of those systems.

## Introduction

SavePoint builds one `net10.0` target that runs on Windows, Linux, and macOS (D9, D26). Code for one operating system lives in the same build as everything else and runs only behind a check of the OS at run time. This standard covers where that code goes, which OS check to use, and how a test that cannot run on an OS skips.

### Purpose

- **Primary:** Every build runs on every OS without a separate target framework. An OS difference is a branch at run time that each OS's gate exercises, not a compile-time switch that only one build ever sees.
- **Secondary:** The per-OS tables (folders, path tokens, the denylist) sit side by side in one place, so a change to one OS shows the others that need it.
- **Side effect:** The compiler's platform analyzer can check Windows-only calls, because they carry `[SupportedOSPlatform]` and sit behind `OperatingSystem.IsWindows()`.

### Scope

C# in `src/` and `tests/`.

## When to Apply

1. **Does the code hold data that differs per OS?** Examples are well-known folders, path tokens, the path-safety denylist, and the process tools.
   - If yes, put it in the OS's `IPlatform` implementation (see "Keep per-OS tables behind `IPlatform`").
   - If no, continue.
2. **Does the code branch on the OS?**
   - If yes, pick the check with "Use the OS check that fits the branch".
   - If no, continue.
3. **Does a test fail on an OS because that OS behaves differently?**
   - If yes, skip it there and list it (see "Skip a test on an OS, and list the skip").
   - If no, this standard does not apply.

**Verification step:** search for the forbidden ways to check the OS:

```bash
grep -rnE --include=*.cs "RuntimeInformation\.IsOSPlatform|Environment\.OSVersion|#if (WINDOWS|LINUX|OSX|MACOS)" src tests
```

The search prints nothing.

## When NOT to Apply

- **Code that behaves the same everywhere.** Use `Path.Combine`, `Path.DirectorySeparatorChar`, and the platform's `PathComparison` instead of a branch. An OS check is only for real differences.
- **A small difference inside shared code.** A one-line guarded call, such as the Windows sharing-violation check in `FileLockProbe.IsSharingViolation`, stays where it is used. Do not move it behind `IPlatform` only to remove the branch.
- **Diagnostics.** `RuntimeInformation.OSDescription` and `OSArchitecture` in the startup log and crash text describe the machine. They are not checks.

## Background

D9 made v2 ship on all three systems. D26 kept one `net10.0` target for OS integration, so notifications and other OS calls use P/Invoke, D-Bus, or a helper program, chosen at run time. `PlatformFactory` detects the OS once and builds `WindowsPlatform`, `LinuxPlatform`, or `MacOSPlatform`.

The gates run on Windows and Linux. macOS is built and unit-tested only through the shared code paths (D9), and has no expected-skip list.

## Coding Standard

### Keep per-OS tables behind `IPlatform`

Data that differs per OS lives in `Platform/Windows`, `Platform/Linux`, or `Platform/MacOS` behind `IPlatform`. Code reads it through the `IPlatform` that `CoreBackend.Create` was given, so a test can pass another OS's platform.

**Correct usage:**

```csharp
public static IPlatform ForCurrentOs()
{
    var environment = PlatformEnvironment.FromCurrentMachine();
    return CurrentOs switch
    {
        OsKind.Windows => new WindowsPlatform(environment),
        OsKind.MacOS => new MacOSPlatform(environment),
        _ => new LinuxPlatform(environment),
    };
}
```

**What to avoid:**

```csharp
// The table lives in the caller, so a test cannot ask for another OS's folders.
var configDir = OperatingSystem.IsWindows() ? @"%APPDATA%\SavePoint" : "~/.config/savepoint";
```

**Examples:**

- `src/Savepoint.Core/Platform/IPlatform.cs`, `IPlatform`: folders, path tokens, the denylist, and process tools for one OS.
- `src/Savepoint.Core/Platform/PlatformFactory.cs`, `PlatformFactory.ForCurrentOs`: picks the implementation.

### Use the OS check that fits the branch

| The branch | The check |
| --- | --- |
| Calls an API that exists on only one OS (P/Invoke, the registry, a Windows-only .NET API) | `OperatingSystem.IsWindows()`, `IsLinux()`, or `IsMacOS()`. Mark the called member `[SupportedOSPlatform]`. The analyzer accepts only these checks as a guard. |
| Changes behavior that a test must run for another OS | `IPlatform.Os` on the injected platform, so a test with a fake platform takes the other branch. |
| Has no `IPlatform` to ask, in App code | `PlatformFactory.CurrentOs`. |

Never detect the OS with `RuntimeInformation.IsOSPlatform`, `Environment.OSVersion`, `#if`, or a string such as `"windows"`.

**Correct usage:**

```csharp
var gog = options.GogInstalls ?? (OperatingSystem.IsWindows() ? new WindowsGogRegistry() : null);
```

**What to avoid:**

```csharp
// The analyzer does not recognize this check, and a compile-time switch hides the code from the other builds.
#if WINDOWS
var gog = new WindowsGogRegistry();
#endif
```

**Examples:**

- `src/Savepoint.Core/Backend/CoreBackend.cs`, `CoreBackend.Create`: guards the Windows-only registry readers.
- `src/Savepoint.Core/Platform/Windows/WindowsSteamRegistry.cs`, `WindowsSteamRegistry.ReadSteamPath`: marked `[SupportedOSPlatform("windows")]`.
- `src/Savepoint.Core/Artwork/GogGalaxyCacheLocator.cs`, `GogGalaxyCacheLocator.ForPlatform`: branches on the injected platform's `Os`.

**Known violations:**

- `src/Savepoint.App/Features/Settings/SettingsEnvironment.cs`, `ProcessFileRevealer.Start`: switches on the strings `"windows"`, `"macos"`, and `"linux"`.

### Skip a test on an OS, and list the skip

A test that cannot run on an OS calls `Assert.SkipUnless` or `Assert.SkipWhen` with the OS check and a reason in plain words, so the report shows it as skipped. It never returns early, which passes without testing anything. Its full name, as the xunit report writes it, goes in the list for each gate that skips it (D48):

| Gate | List |
| --- | --- |
| Windows (`build/gate.ps1`, the Windows CI job) | `build/expected-skips/windows.txt` |
| Linux CI (`build/gate.sh` in the CI container) | `build/expected-skips/linux-ci.txt` |
| Linux under WSL (`build/gate-linux.ps1`) | `build/expected-skips/linux-wsl.txt`, which includes `linux-ci.txt` and adds only its own extras |

A line that starts with `~` marks a skip that depends on timing. The gate fails on a skip that its list does not name. After a full gate run, `tools/SkipCheck` with `--write` rewrites a list from the run's reports (see `docs/developer-guide.md`, section "Gates").

**Correct usage:**

```csharp
Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to delete a file another program holds open");
```

**What to avoid:**

```csharp
if (!OperatingSystem.IsWindows())
{
    return; // Only Windows refuses to delete a file another handle holds open.
}
```

**Examples:**

- `tests/Savepoint.Core.Tests/Services/Storage/StorageFolderDeleteTests.cs`, `A_file_another_program_holds_open_still_fails_as_a_file_in_use`: an OS skip with its reason.
- `tools/SkipCheck/SkipVerdict.cs`, `SkipVerdict.Compare`: fails a skip that the list does not name.

## Additional Resources

### Project Documentation

- `docs/decisions.md`, D9 (all three systems), D26 (one target framework), D48 (expected skips).
- `docs/developer-guide.md`, section "Gates": how the skip check runs.
- `docs/testing-on-mac-and-linux.md`: how to check by hand the desktop behavior the gates cannot, such as notifications and single instance, on a Mac and a Linux VM.
- [Start processes with an argument list](csharp-process-argument-list.md): the per-OS openers.
- [Test names and structure](tests-naming-and-structure.md): the rest of the test conventions.

### External Resources

- [Platform compatibility analyzer](https://learn.microsoft.com/dotnet/standard/analyzers/platform-compat-analyzer)
