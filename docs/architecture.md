# SavePoint v2 - architecture

How the code is organized and what each layer promises the one above it. Behavior details live in `../Savepoint/plan.md` and `docs/reference/`; departures in `docs/decisions.md`. When this file and a reference doc disagree, this file and `decisions.md` win.

## Layers

```
Savepoint.App (Avalonia)
  Views (.axaml) -> ViewModels -> Api (IGamesApi, IProfilesApi, ILaunchApi, ISettingsApi, IJobsApi, IHistoryApi, IDialogs)
                                     |
Savepoint.Core                       v
  Api/        facade: validates input, calls services, converts every exception to ErrorInfo (OperationResult<T>)
  Services/   GameService, LaunchOrchestrator, SessionRegistry, JobRegistry, GameLock, StorageService, ...
  Catalog/    models, validation, CatalogStore (atomic JSON), templates
  Git/        GitBinary, GitRunner (process), GitEngine (operations on a RepoRef)
  Paths/      PathTokenResolver, PathSafety, ConsentMarker
  Platform/   IPlatform + Windows/Linux/MacOS implementations
  Errors/     SavepointException hierarchy, ErrorInfo
```

Rules:
- Views bind only to view models. View models call only the Api interfaces and `IDialogs` (folder pickers, file pickers, confirmations). Nothing in `Savepoint.App` references `Git/`, `Paths/`, or `Services/` types directly, except the composition root.
- `Savepoint.Core` has no Avalonia reference and no static mutable state. Everything is constructed in one composition root (`AppComposition` in the App) and injected, so tests build the same graph against temp directories.
- Every file-system or process side effect in Core goes through an injected abstraction where tests need to fake it (`IPlatform`, `IClock`, `IProcessInspector`, `IGameLauncher`); plain temp-dir IO is used everywhere else.
- Async all the way: every operation that touches disk or git is `async Task` and takes a `CancellationToken`.

## Errors

`SavepointException` (abstract) carries `MessageId` (a catalog key such as `error.pathSafety.tier1Blocked`), `Args` (`IReadOnlyDictionary<string, object?>` for ICU placeholders and extras such as `path`, `role`, `conflictingGameId`), and the English `Message` (dev-facing, logged, shown only behind "Show details").

Subclasses and ids (keep the v1 ids from `docs/reference/v1-ui.md` section 1.2, plus the new ones marked *new*):

| Exception | MessageId | Args |
|---|---|---|
| PathSafetyException | `error.pathSafety.tier1Blocked` / `tier2Blocked` | `path`, `tier` |
| ConsentMarkerException | `error.consentMarker.missing` / `conflict` | `path`, `role`, `existingGameId?`, `existingRole?` |
| DiskSpaceException | `error.diskSpace.low` | `path`, `requiredBytes`, `freeBytes` |
| GitOperationException | `error.gitOperation.checkoutFailed` | `path?` (the file that stayed locked) |
| AmendNotAvailableException | `error.gitOperation.amendNotAvailable` | |
| OperationCancelledByUserException | `error.capture.cancelled` / `error.compaction.cancelled` / `error.launch.canceled` / `error.storageRelocation.cancelled` *new* | `gameId?` |
| GameOverlapException | `error.gameService.pathOverlap` | `conflictingGameId`, `conflictingGameName` |
| ProfileActiveException | `error.gameService.profileActive` | `profileId` |
| ReconnectableGameException | `error.gameService.reconnectable` | `existingGameId`, `roles`, `branchNames` |
| GameRunningException *new* | `error.gameService.gameRunning` | `gameId`, `gameName` |
| LaunchAlreadyRunningException | `error.launch.alreadyRunning` | `gameId` |
| NoActiveSessionException | `error.launch.noActiveSession` | `gameId` |
| SessionStillRunningException | `error.launch.sessionStillRunning` | `gameId` |
| DirtyWorkingTreeException | `error.launch.dirtyWorkingTree` | `role` |
| DetachedCaptureNotSupportedException | `error.launch.detachedCaptureNotSupported` | `role` |
| LaunchSpawnException | `error.launch.spawnFailed` | `method` |
| PathMissingException | `error.path.missing` | `path`, `role` |
| StorageDestinationNotEmptyException | `error.storageRelocation.destinationNotEmpty` | `path` |
| ValidationException | `error.validation.invalidPayload` | `issues` (list of `{ path, messageId }`) |
| CatalogCorruptException *new* | `error.catalog.corrupt` | `path` |
| GitUnavailableException *new* | `error.git.unavailable` | `path` |

`ErrorInfo(string MessageId, IReadOnlyDictionary<string, object?> Args, string Detail)` is the only error shape that leaves Core. `ErrorInfo.From(Exception)` maps a `SavepointException` to its id and args, `OperationCanceledException` from a user cancel to the matching cancelled id, and anything else to `error.unknown` with the exception text as `Detail`.

## Platform

```csharp
public interface IPlatform
{
    OsKind Os { get; }                                // Windows, Linux, MacOS
    StringComparison PathComparison { get; }          // OrdinalIgnoreCase on Windows/macOS, Ordinal on Linux
    AppDirectories Directories { get; }               // CatalogDir, DefaultStorageRoot, LogsDir, HomeDir
    IReadOnlyDictionary<string, string> PathTokens { get; } // documents, appData, localAppData, userHome, savedGames (where it exists)
    PathDenylist Denylist(IEnumerable<string> extraBlockedRoots); // tier 1 (subtree) + tier 2 (exact) absolute paths
    IProcessInspector Processes { get; }              // list running processes (pid, name, executable path)
    IGameLauncher Launcher { get; }                   // start an executable or app bundle; open a URI
}
```

One implementation per OS under `Platform/Windows`, `Platform/Linux`, `Platform/MacOS`. `PlatformFactory.ForCurrentOs()` picks one. Tests may construct any implementation with an injected home directory and environment so all three tables are testable on any OS.

## Git

- `GitBinary`: locates the bundled git under `<AppContext.BaseDirectory>/git/` (override: `SAVEPOINT_GIT_ROOT` for tests and dev) and builds the base environment for the platform's bundle layout (MinGit on Windows: `cmd/git.exe`; dugite-native elsewhere: `bin/git` with `GIT_EXEC_PATH`, `GIT_TEMPLATE_DIR`, `GIT_CONFIG_SYSTEM`/`PREFIX`). `VerifyAsync()` runs `git --version` and throws `GitUnavailableException` when it cannot.
- `GitRunner`: runs one git process with an argument list (never a shell string), `GIT_DIR`/`GIT_WORK_TREE`, the pinned identity and config isolation (D13.24), `GIT_OPTIONAL_LOCKS=0` for read-only calls, optional stdin, line-streamed stdout/stderr callbacks, or binary stdout. Cancellation kills the whole process tree (`Process.Kill(entireProcessTree: true)`). Non-zero exit throws `GitCommandException` (internal; the engine maps it to a typed error).
- `GitEngine`: the operations in `docs/reference/v1-engine.md` on a `RepoRef(string GitDir, string WorkTree, string GameId, RepoRole Role)`. Every operation that writes checks path safety and the consent marker first.

## Catalog

Models are immutable records with `System.Text.Json` source-generated serialization (camelCase, matching v1's JSON shapes so a v1 catalog could be read): `GameDefinition`, `ExternalLocation`, `LaunchConfig`, `Profile`, `AppSettings`. Enums serialize as their v1 strings (`"exe"`, `"uri"`, `"saves"`, `"system"`, `"sidebar"`, `"blue"`, ...).

`CatalogStore` owns `games.json`, `profiles.json`, `settings.json` in `IPlatform.Directories.CatalogDir`: one async lock, atomic write (temp file in the same directory, flush, `File.Move(overwrite: true)`), `.bak` of the previous file, and `CatalogCorruptException` on unreadable content (D13.2).

## Services and the Api facade

Services hold the behavior; the facade is thin. The facade interfaces are what the UI and its tests depend on, so they are the contract between the Core and App tracks:

```csharp
public sealed record OperationResult<T>(T? Value, ErrorInfo? Error) { public bool Ok => Error is null; }
public sealed record JobStarted(Guid JobId);

public interface IGamesApi
{
    Task<OperationResult<IReadOnlyList<GameDefinition>>> ListAsync();
    Task<OperationResult<GameDefinition>> SaveAsync(GameDefinition definition, bool isNew);  // validates, consent, overlap, orphan detection
    Task<OperationResult<GameDefinition>> ResolveOrphanAsync(GameDefinition definition, OrphanAction action);
    Task<OperationResult<bool>> DeleteAsync(string gameId);
    Task<OperationResult<ConsentStatus>> GetConsentStatusAsync(string gameId, RepoRole role);
    Task<OperationResult<StorageUsage>> GetStorageUsageAsync(string gameId);
    Task<OperationResult<CapturePreview>> GetCapturePreviewAsync(string gameId, CancellationToken ct);
    Task<OperationResult<IReadOnlyList<ProfileRowStats>>> GetProfileRowStatsAsync(string gameId, CancellationToken ct);
    Task<OperationResult<JobStarted>> OptimizeStorageAsync(string gameId);
    Task<OperationResult<string>> ResolvePathAsync(string rawPath);                 // token expansion for the editor hint
    Task<OperationResult<IReadOnlyList<string>>> ListExecutablesAsync(string installPath, CancellationToken ct);
    Task<OperationResult<GameDefinition>> ImportDefinitionAsync(string filePath);   // read + validate, never saves
    Task<OperationResult<bool>> ExportDefinitionAsync(string gameId, string filePath);
    IReadOnlyList<GameTemplate> Templates { get; }
}

public interface IProfilesApi
{
    Task<OperationResult<IReadOnlyList<Profile>>> ListAsync(string? gameId = null);
    Task<OperationResult<Profile>> CreateAsync(string gameId, string name, string? notes);         // generates id + branch name
    Task<OperationResult<Profile>> UpdateAsync(Profile profile);                                    // rename, notes, version override
    Task<OperationResult<bool>> DeleteAsync(string gameId, string profileId);
    Task<OperationResult<Profile>> DuplicateAsync(string gameId, string sourceProfileId, string name);
    Task<OperationResult<JobStarted>> CaptureAsync(string gameId, string profileId, string message, CaptureMode mode, RepoRoles roles);
    Task<OperationResult<JobStarted>> KeepAsNewProfileAsync(string gameId, string name);            // create + capture both folders
}

public interface ILaunchApi
{
    Task<OperationResult<LaunchPreview>> GetPreviewAsync(string gameId, string profileId, string? saveId, CancellationToken ct);
    Task<OperationResult<JobStarted>> StartAsync(string gameId, string profileId, string? saveId);
    Task<OperationResult<bool>> CancelAsync(string gameId);
    Task<OperationResult<bool>> MarkFinishedAsync(string gameId);
    Task<OperationResult<JobStarted>> CompleteSessionAsync(string gameId, SessionCompletion completion);
    Task<OperationResult<bool>> DiscardChangesAsync(string gameId, RepoRole role);
    LaunchSessionInfo? GetSession(string gameId);
    event EventHandler<LaunchSessionChanged>? SessionChanged;    // started, running, exited (with play duration), completed
}

public interface ISettingsApi
{
    Task<OperationResult<AppSettings>> GetAsync();
    Task<OperationResult<AppSettings>> SaveAsync(AppSettings settings);
    Task<OperationResult<JobStarted>> RelocateStorageAsync(string newRoot);
    Task<OperationResult<StorageSummary>> GetStorageSummaryAsync();   // total used by all games, free space, last optimized
    Task<OperationResult<bool>> ClearAllDataAsync();                   // D8
}

public interface IHistoryApi
{
    Task<OperationResult<GameHistory>> GetAsync(string gameId, CancellationToken ct);   // versions & mods points + saves, with profile names and "in use" markers
    Task<OperationResult<JobStarted>> GoBackToAsync(string gameId, HistoryPointId point); // launches/restores through the launch flow
}

public interface IJobsApi
{
    IReadOnlyList<JobSnapshot> List();
    Task<OperationResult<bool>> CancelAsync(Guid jobId);
    event EventHandler<JobEvent>? JobChanged;    // raised on a background thread; the App marshals to the UI thread
}
```

DTO shapes follow `docs/reference/v1-ui.md` section 1.4 (`CapturePreview`, `ProfileRowStats`, `LaunchPreview`, `StorageUsage`) and section 1.5 (job kinds `launch`, `capture`, `compact`, `relocateStorage`; progress `{ filesDone?, filesTotal?, bytesDone?, bytesTotal?, currentPath? }`; 5 completed jobs kept; 200 recent lines). Capture and optimization results are per folder (`FolderOutcome` with `Ok` + `ErrorInfo?`), never a single success flag (D13.20).

## App

- `AppComposition` builds Core against the real platform and wires the Api implementations into view models.
- `ShellViewModel` owns navigation (Games, History, Settings), layout, and the selected game. `LaunchFlowViewModel` is owned by the shell, not a screen (D13.19).
- `Localization/`: `LocalizationCatalog` (ICU formatting via MessageFormat), `{loc:Loc}` markup extension, `L.Format(key, args)` for view models. Message ids from `ErrorInfo` are catalog keys.
- `Theme/`: Material color port (`Theme/Material/`), `ThemeService` (seed + mode -> resource dictionary of MD3 roles, live switching), styles and control themes keyed to role names, fonts under `Assets/Fonts`.
- `Platform/`: OS integration that needs the UI stack (notifications, folder reveal, single instance).
