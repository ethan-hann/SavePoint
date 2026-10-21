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

Services hold the behavior; the facade is thin. The interfaces in `src/Savepoint.Core/Api/` are the contract between the Core and App tracks: view models depend only on them, and `tests/Savepoint.App.Tests/Fakes/FakeBackend` implements all of them in memory (scripted jobs and sessions, a call log, one-shot failures, a scenario builder whose default is the mockup data) so screens can be built and tested before the services exist.

Rules that hold for every member:
- Every async call answers `OperationResult<T>`: `Ok` with a non-null `Value`, or `Error` (`ErrorInfo`). No exception crosses the boundary. A call with nothing to return answers `OperationResult<Unit>`. `OperationResult.Failure<T>(Exception)` maps through `ErrorInfo.From`, so the facade and the fake report an exception the same way.
- Reads take a `CancellationToken`. Long work starts a job and returns `JobStarted` at once; it is cancelled with `IJobsApi.CancelAsync`, so job starters take no token. Other mutations take no token either, because stopping one halfway is worse than finishing it.
- `IJobsApi.JobChanged` and `ILaunchApi.SessionChanged` are raised on a background thread with the whole snapshot after each change; the App marshals them to the UI thread. Each snapshot has a `Revision` that grows with every change. A view model subscribes first, then reads the snapshot getters, and drops an event whose revision is not newer than the one it holds, so a late subscriber never loses a result.
- Snapshot getters (`List`, `Get`, `GetSession`, `ListSessions`, `Templates`) are synchronous, read memory, and cannot fail, so they return plain values.

```csharp
public interface IGamesApi
{
    IReadOnlyList<GameTemplate> Templates { get; }
    Task<OperationResult<IReadOnlyList<GameDefinition>>> ListAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<AddGameOutcome>> AddAsync(GameDefinition definition);              // Added(game) | HistoryFound(orphan)
    Task<OperationResult<GameDefinition>> ResolveOrphanAsync(GameDefinition definition, OrphanResolution resolution);
    Task<OperationResult<GameDefinition>> UpdateAsync(GameDefinition definition);           // also re-points a moved folder
    Task<OperationResult<Unit>> DeleteAsync(string gameId);
    Task<OperationResult<IReadOnlyList<FolderStatus>>> GetFolderStatusAsync(string gameId, CancellationToken cancellationToken = default);
    Task<OperationResult<GameStorageUsage>> GetStorageUsageAsync(string gameId, CancellationToken cancellationToken = default);
    Task<OperationResult<CapturePreview>> GetCapturePreviewAsync(string gameId, CancellationToken cancellationToken = default);
    Task<OperationResult<JobStarted>> OptimizeStorageAsync(string gameId);
    Task<OperationResult<string>> ResolvePathAsync(string rawPath, CancellationToken cancellationToken = default);
    Task<OperationResult<IReadOnlyList<string>>> ListExecutablesAsync(string installPath, CancellationToken cancellationToken = default);
    Task<OperationResult<GameDefinition>> ImportDefinitionAsync(string filePath, CancellationToken cancellationToken = default);  // never saves
    Task<OperationResult<Unit>> ExportDefinitionAsync(string gameId, string filePath, CancellationToken cancellationToken = default);
}

public interface IProfilesApi
{
    Task<OperationResult<IReadOnlyList<Profile>>> ListAsync(string? gameId = null, CancellationToken cancellationToken = default);
    Task<OperationResult<IReadOnlyList<ProfileRowStats>>> GetRowStatsAsync(string gameId, CancellationToken cancellationToken = default);
    Task<OperationResult<JobStarted>> CreateAsync(string gameId, string name, string? notes, string pointName);  // wizard and keep-as-new
    Task<OperationResult<Profile>> UpdateAsync(string gameId, string profileId, ProfileChanges changes);
    Task<OperationResult<Profile>> DuplicateAsync(string gameId, string sourceProfileId, string name);
    Task<OperationResult<ProfileDeletePreview>> GetDeletePreviewAsync(string gameId, string profileId, CancellationToken cancellationToken = default);
    Task<OperationResult<Unit>> DeleteAsync(string gameId, string profileId);
    Task<OperationResult<JobStarted>> CaptureAsync(string gameId, string profileId, string pointName, IReadOnlyCollection<RepoRole>? folders = null);
}

public interface ILaunchApi
{
    event EventHandler<LaunchSessionInfo>? SessionChanged;
    LaunchSessionInfo? GetSession(string gameId);
    IReadOnlyList<LaunchSessionInfo> ListSessions();
    Task<OperationResult<LaunchPreview>> GetPreviewAsync(string gameId, LaunchTarget target, CancellationToken cancellationToken = default);
    Task<OperationResult<JobStarted>> StartAsync(string gameId, LaunchTarget target);
    Task<OperationResult<Unit>> DiscardChangesAsync(string gameId, RepoRole folder);
    Task<OperationResult<Unit>> MarkFinishedAsync(string gameId);                            // "I'm done playing"
    Task<OperationResult<Unit>> CompleteSessionAsync(string gameId, SessionCompletion completion);
}

public interface ISettingsApi
{
    Task<OperationResult<AppSettings>> GetAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<AppSettings>> SaveAsync(AppSettings settings);                      // keeps StorageRoot and LastOptimizedAt
    Task<OperationResult<StorageSummary>> GetStorageSummaryAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<JobStarted>> OptimizeAllStorageAsync();
    Task<OperationResult<StorageRelocationPreview>> GetRelocationPreviewAsync(string newRoot, CancellationToken cancellationToken = default);
    Task<OperationResult<JobStarted>> RelocateStorageAsync(string newRoot);
    Task<OperationResult<ClearAllDataPreview>> GetClearAllDataPreviewAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<JobStarted>> ClearAllDataAsync();                                   // D8
}

public interface IHistoryApi
{
    Task<OperationResult<GameHistory>> GetAsync(string gameId, string? profileId = null, CancellationToken cancellationToken = default);
}

public interface IJobsApi
{
    event EventHandler<JobSnapshot>? JobChanged;
    IReadOnlyList<JobSnapshot> List();
    JobSnapshot? Get(Guid jobId);
    Task<OperationResult<Unit>> CancelAsync(Guid jobId);
}

public interface IAppStatusApi
{
    Task<OperationResult<StartupStatus>> CheckAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<string>> RecoverCatalogFileAsync(string path, CatalogRecovery recovery, CancellationToken cancellationToken = default);
}
```

Catalog models (`GameDefinition`, `Profile`, `AppSettings`, `GameTemplate`, `RepoRole`) are reused as they are; the Api adds only what the catalog does not hold.

**Jobs.** `JobKind`: `Launch` (prepare, switch, start the game), `Capture`, `Compact`, `RelocateStorage`, `ClearData`. `JobStatus`: `Running`, `Succeeded`, `Partial`, `Failed`, `Cancelled`. `JobSnapshot` carries the kind, the game (null for jobs across every game), status, `Cancellable` (every kind but `ClearData`, D13.18), `CancelRequested`, `JobProgress { FilesDone, FilesTotal, BytesDone, BytesTotal, CurrentPath }` (each report replaces the last), the newest 200 `RecentLines`, start and finish times, the `JobResult`, and the `ErrorInfo` of a failed or cancelled job. `List()` keeps running jobs plus the five most recently finished. Results by kind: `CaptureResult(ProfileId, PointId?, Folders)`, `OptimizeResult(Games)` with a `GameOptimizeOutcome(GameId, Folders, BytesFreed?)` per game, `StorageRelocationResult(PreviousRoot, NewRoot, BytesCopied)`, `ClearAllDataResult(BytesFreed, NotRemoved)`; a launch job has none. Folder work reports one `FolderOutcome(Role, Error?)` per folder it touched: all fine is `Succeeded`, all failed is `Failed` (or `Cancelled` when every failure is a cancellation), anything between is `Partial`, never success (D13.20).

**Launch.** `LaunchTarget` is closed: `ProfileTip(ProfileId)`, `Save(ProfileId, SaveId)` for the dialog's save picker, and `Point(PointId)` for "Go back to this" (D15), which therefore gets the same preview and dirty checks as Play. `LaunchPreview` holds the resolved profile, a `FolderLaunchState` per folder (matches target, dirty and how many files, the owning profile or null when nothing can claim the changes, and a `SwitchSummary` of files, mod files, bytes, and an estimated duration), the current and target `InstallState` (version and mod-file count, for "going from 1.6.1170 with no mods back to 1.6.640 with 189"), the target profile's saves newest first with a most-recent marker and the selected one, and the `ShowBanner`, `OnlySaveDiffers`, and `TargetIsOlderPoint` flags. `StartAsync` returns the launch job, which ends once the game runs; the session then lives in `LaunchSessionInfo` (game, profile, target, `StartedFromOlderPoint`, launch job id, `Revision`) whose closed `SessionState` is `Preparing`, `Switching(progress)`, `Running(startedAt)`, `Exited(startedAt, playDuration, newFileCount, changedInstallFileCount, options)`, `Completing(completion, jobId)`, `Completed(completion, result?, error?)`, or `Failed(error)`. Cancelling the switch is `IJobsApi.CancelAsync` on the launch job. `PostPlayOptions` lists the choices the session allows, the default, the point `UpdateExisting` would replace, and how many newer points leave the profile on `MoveForward` (D14's second confirmation). `SessionCompletion` is `CaptureNew(pointName)`, `UpdateExisting(pointName)`, `ForkNewProfile(profileName, pointName)`, `MoveForward(pointName)`, `Skip`; the point name is D14's message. Skip completes at once; every other choice runs a capture job. A finished or failed session stays visible until the next launch of that game.

**Profiles.** `ProfileRowStats`: active marker, folders with unsaved changes, save count (null without a saves folder), mod-file count, detected version and override, last played, running. `ProfileChanges` carries only the fields a user edits. `ProfileDeletePreview` counts the points only that profile holds, in total and per folder (D17), and carries `BlockedBy`, the error the delete would fail with (active profile, running game).

**Games.** `AddGameOutcome` is `Added(game)` or `HistoryFound(OrphanedHistory)`: finding old history is a question for the user, not an error. `OrphanedHistory` names the old game id, the folders, and each old profile with a suggested name or null (the UI supplies fallback copy); `OrphanResolution` is `StartFresh` or `Reconnect(existingGameId, profiles)` with the names the user will see. `FolderStatus` gives each folder's stored and resolved path, whether it exists (the "moved or is gone" card), and its `ConsentStatus(Confirmed, ConfirmedAt)`. `CapturePreview` lists each folder's path, file count, mod-file count, and bytes, plus the bytes a capture would add after reuse.

**History.** `GameHistory` lists `HistoryPoint`s newest first, optionally for one profile: id, kind (`VersionModsAndSave`, `VersionAndMods`, `SaveOnly`, D15), name, date, profile id and name, the install state at that point, mod files changed since the profile's previous install point, bytes where known, and the `InUse` and `LoadedNow` markers. A `VersionModsAndSave` point belongs in both columns of the list view.

**Storage and settings.** `GameStorageUsage` and `StorageSummary` give SavePoint's bytes per game and in total and the storage volume's size and free space; the summary adds the last optimization date. `StorageRelocationPreview` and `ClearAllDataPreview` list what will move or go, with sizes and a `BlockedBy` error, so the confirmation is exact; the previous storage folder is left in place after a move.

**Startup (D18).** `StartupStatus` holds `GitStatus(Available, Path, Version, Error)` and `CatalogStatus(Directory, CorruptFiles)`, each `CorruptCatalogFile` naming its path, its last good backup if any, and the error. `RecoverCatalogFileAsync` restores the backup or starts that file empty; either way the corrupt file is moved aside, never deleted, and the call answers with where it went.

## App

- `AppComposition` builds Core against the real platform and wires the Api implementations into view models.
- `ShellViewModel` owns navigation (Games, History, Settings), layout, and the selected game. `LaunchFlowViewModel` is owned by the shell, not a screen (D13.19).
- `Localization/`: `LocalizationCatalog` (ICU formatting via MessageFormat), `{loc:Loc}` markup extension, `L.Format(key, args)` for view models. Message ids from `ErrorInfo` are catalog keys.
- `Theme/`: Material color port (`Theme/Material/`), `ThemeService` (seed + mode -> resource dictionary of MD3 roles, live switching), styles and control themes keyed to role names, fonts under `Assets/Fonts`.
- `Platform/`: OS integration that needs the UI stack (notifications, folder reveal, single instance).
