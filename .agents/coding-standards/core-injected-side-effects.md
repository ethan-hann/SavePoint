---
paths:
  - "src/Savepoint.Core/**/*.cs"
---

# Injected side effects in Core

- **Status:** accepted
- **Date Created:** 2026-10-26 17:25
- **Last Updated:** 2026-10-26 18:10
- **Applies To:**
  - Any Core code that reads the clock, the environment, a running process, free disk space, or the network.
  - Any static field in `Savepoint.Core` whose value or contents change after the type loads.

## Introduction

Core gets its clock, its environment, and its process and network access from whoever builds it. `CoreBackend.Create` builds the whole graph at startup from `BackendOptions`, and a test builds the same graph with fakes and temp folders. This standard covers which effects come in through a seam, which do not, and the one kind of static state Core allows.

### Purpose

- **Primary:** A test can build the real Core graph and control every effect that would make it slow, flaky, or unsafe: the time, the machine's variables, other running processes, disk space, and the network.
- **Secondary:** Two backends in one test run never share state, because Core holds none in statics.
- **Side effect:** The constructor of a class lists everything outside the process that the class depends on.

### Scope

C# in `src/Savepoint.Core/`. App code gets its clock from `FeatureContext.Clock`, which follows the same idea.

## When to Apply

1. **Does the code read the wall clock?** Look for `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, or `DateTimeOffset.UtcNow`.
   - If yes, take a `TimeProvider` (see "Take the clock as a `TimeProvider`").
   - If no, continue.
2. **Does the code read an environment variable or a known folder, inspect a process SavePoint did not start, check free disk space, or call the network?**
   - If yes, go through the matching seam (see "Reach the machine through its seam").
   - If no, continue.
3. **Does the code add a static field whose value or contents change after the type loads?** A `static readonly` collection that gains or loses entries counts.
   - If yes, and the field holds a handle to an OS resource that must live as long as the process, see "Exception - process-lifetime OS handles".
   - If yes otherwise, make it an instance field on a class that `CoreBackend.Create` builds.
   - If no, this standard does not apply.

**Exception - process-lifetime OS handles:** A static may hold a handle to an OS resource that must exist exactly once and live as long as the process, and so may the record of what that resource holds. The Windows job object in `GitProcessJob`, which ends every git when SavePoint dies (D59), and the `GitProcessRegistry` of running gits are the cases this covers. A comment on each such field says why it is static.

**Verification step:** search Core for a wall clock:

```bash
grep -rnE --include=*.cs "DateTime(Offset)?\.(Now|UtcNow)" src/Savepoint.Core
```

The search prints nothing after the known violations below are fixed.

## When NOT to Apply

- **Reading and writing files.** Core uses `File` and `Directory` directly. Only the root folders come in through `BackendOptions` (`CatalogDirectory`, `InstallDirectory`, and others), and tests point them at temp folders. Do not add a file-system interface.
- **Measuring elapsed time.** A `Stopwatch` that times a wait or throttles progress measures a duration, not the time of day. It needs no `TimeProvider`.
- **A value computed once from read-only data.** A `static readonly` table that is built once and never changes is not mutable state.
- **The places where the real machine enters.** `CoreBackend.Create`, `PlatformEnvironment.FromCurrentMachine`, the `IPlatform` implementations, and the factory methods they call (`GitBinary.Locate`, `GogGalaxyCacheLocator.ForPlatform`) read the machine directly by design. They pass what they read into constructors, so the class that uses it can still be tested.
- **Processes SavePoint starts.** The code can follow a git or a game that SavePoint started, by its process id, and can build the environment a child inherits. Both are part of starting the process.

## Background

`docs/architecture.md` (section "Layers") states that Core has no static mutable state and that every side effect tests must fake goes through an injected abstraction, with plain temp-folder IO used everywhere else. The clock is the .NET `TimeProvider` type. A test passes `ManualTimeProvider` (Core tests) or `FixedTimeProvider` (App tests) to move time by hand.

## Coding Standard

### Take the clock as a `TimeProvider`

A class that needs the time takes `TimeProvider? timeProvider = null` and falls back to `TimeProvider.System`. `CoreBackend.Create` passes `BackendOptions.Time` down to it.

**Correct usage:**

```csharp
public JobRegistry(TimeProvider? timeProvider = null, IDiagnosticLog? log = null)
{
    _time = timeProvider ?? TimeProvider.System;
    _log = log ?? DiagnosticLog.None;
}
```

**What to avoid:**

```csharp
// No test can move this clock, so the age check cannot be tested.
if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(folder) > _leftoverAge)
```

**Examples:**

- `src/Savepoint.Core/Services/Jobs/JobRegistry.cs`, `JobRegistry` constructor: the clock defaults to the system clock.
- `src/Savepoint.Core/Backend/BackendOptions.cs`, `BackendOptions.Time`: where a test sets the clock for the whole backend.

### Reach the machine through its seam

Use the seam that exists for each effect. Add a new seam only for an effect that has none, and wire it through `BackendOptions` or `IPlatform`.

| Effect | Seam |
| --- | --- |
| Environment variables, home folder | `PlatformEnvironment.GetVariable`, `PlatformEnvironment.HomeDirectory` |
| Known folders (app data, documents) | `IPlatform.Directories`, or a value the factory passes into the constructor |
| Processes SavePoint did not start | `IProcessInspector` |
| Starting a game or opening a link | `IGameLauncher`, `IProcessStarter` |
| Free disk space | `IDiskSpace` |
| HTTP | `BackendOptions.ArtworkHttp` (an `HttpMessageHandler`) |

**Correct usage:**

```csharp
/// <summary>Looks up an environment variable; null when it is unset.</summary>
public Func<string, string?> GetVariable { get; init; } = static _ => null;
```

**What to avoid:**

```csharp
// Reads the real machine, so a test sees whatever the developer's shell has set.
var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
```

**Examples:**

- `src/Savepoint.Core/Platform/PlatformEnvironment.cs`, `PlatformEnvironment.GetVariable`: the environment seam. `PlatformEnvironment.FromCurrentMachine` is the one place it reads the real machine.
- `src/Savepoint.Core/Backend/CoreBackend.cs`, `CoreBackend.Create`: where each seam gets its real or fake value.

### Keep state on instances, not statics

State that changes belongs to an instance that `CoreBackend.Create` builds. Two backends in one process, as the contract tests build, then never see each other's state.

**Correct usage:**

```csharp
// JobRegistry: each backend builds its own registry, so two backends in a test never share jobs.
private readonly Dictionary<Guid, Job> _byId = [];
```

**What to avoid:**

```csharp
// Every backend in the test process shares this cache.
private static readonly Dictionary<string, GameDefinition> _cache = new();
```

**Correct usage (the process-lifetime exception):**

```csharp
// Created on first use and never closed; closing it is what would end the processes in it.
private static readonly Lazy<(IntPtr Handle, string? Problem)> _job = new(Create, LazyThreadSafetyMode.ExecutionAndPublication);
```

**Examples:**

- `src/Savepoint.Core/Git/GitProcessJob.cs`, `GitProcessJob._job`: a process-lifetime OS handle with the comment that justifies it.

**Known violations:**

- `src/Savepoint.Core/Git/GitProcessRegistry.cs`, `GitProcessRegistry._shared` and `GitProcessRegistry._forApp`: statics under the exception, but with no comment on the fields that says why they are static.

## Additional Resources

### Project Documentation

- `docs/architecture.md`, section "Layers": the rules for Core's dependencies.
- `docs/decisions.md`, D59 (no git keeps running after SavePoint is stopped from outside).
- [Api results and errors](core-api-results-and-errors.md): what an Api member returns.
- [Platform-specific code](csharp-platform-specific-code.md): where code for one OS goes.
- [Test names and structure](tests-naming-and-structure.md): temp folders and fakes in tests.

### External Resources

- [TimeProvider](https://learn.microsoft.com/dotnet/api/system.timeprovider)
