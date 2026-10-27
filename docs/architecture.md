# SavePoint v2 - architecture

How the code is organized and what each layer promises the one above it. The reasons are in `docs/decisions.md`. Where the v1 spec disagrees, this file and `decisions.md` win.

## Layers

```
Savepoint.App (Avalonia)
  Views (.axaml) -> ViewModels -> Api (IGamesApi, IProfilesApi, ILaunchApi, ISettingsApi, IHistoryApi, IJobsApi, IAppStatusApi, IArtworkApi) + IDialogs
                                     |
Savepoint.Core                       v
  Api/        facade: validates input, calls services, converts every exception to ErrorInfo (OperationResult<T>)
  Backend/    CoreBackend.Create(BackendOptions): builds every service and the Api implementations
  Services/   Games, Profiles, History, Settings, Storage, AppStatus, Jobs (JobRegistry), Locking (GameLock), Exe (version reader, scanner)
  Launch/     per-OS game launchers, process starter, session watcher (exit detection); Orchestration/ LaunchOrchestrator (ILaunchApi) and the session book (IGameSessions)
  Artwork/    IArtworkApi (D30): SteamAppIdDetector, SteamFolderLocator, SteamLocalCache, SteamCdn, ImageProbe, ArtworkStore
  Diagnostics/ IDiagnosticLog (D34): what Core logs; BackendOptions.Log, no-op by default
  Catalog/    models, validation, CatalogStore (atomic JSON), templates
  Git/        GitBinary, GitRunner (process), GitEngine (operations on a RepoRef)
  Paths/      PathTokenResolver, PathSafety, ConsentMarker
  Platform/   IPlatform + Windows/Linux/MacOS implementations
  Errors/     SavepointException hierarchy, ErrorInfo
```

- Views bind only to view models. View models call only the Api interfaces and `IDialogs` (folder pickers, file pickers, confirmations).
- Nothing in `Savepoint.App` references `Git/`, `Paths/`, or `Services/` types directly. `Hosting/AppBootstrap` reaches them only through `CoreBackend.Create` and the launch orchestrator.
- `Savepoint.Core` has no Avalonia reference and no static mutable state, except a handle to an OS resource that must live as long as the process (the git job object, D59). Everything else is constructed at startup and injected, so tests build the same graph against temp directories.
- Every side effect in Core that tests must fake goes through an injected abstraction (`IPlatform`, `TimeProvider`, `IProcessInspector`, `IGameLauncher`, `IDiskSpace`). Plain temp-dir IO is used everywhere else.
- Async all the way: every operation that touches disk or git is `async Task`. Api reads take a `CancellationToken`. A long job is cancelled through `IJobsApi.CancelAsync`, and the job's own token flows all the way down through its work. A short mutation takes no token, because stopping it halfway is worse than finishing it.

## Errors

`SavepointException` (abstract) carries:
- `MessageId`: a catalog key such as `error.pathSafety.tier1Blocked`.
- `Args`: an `IReadOnlyDictionary<string, object?>` for ICU placeholders and extras such as `path`, `role`, `conflictingGameId`.
- `Message`: English text for developers. It is logged and never shown. The error card's "Copy details for support" copies it with git's output (D48.3).

| Exception | MessageId | Args |
|---|---|---|
| PathSafetyException | `error.pathSafety.tier1Blocked` / `tier2Blocked` | `path`, `tier` |
| ConsentMarkerException | `error.consentMarker.missing` / `conflict` | `reason`, `path`, `role`, `existingGameId?`, `existingRole?` |
| DiskSpaceException | `error.diskSpace.low` | `path`, `requiredBytes` (for a snapshot: the floor plus its new and changed bytes), `freeBytes` |
| GitOperationException | `error.gitOperation.checkoutFailed` | `path?` (the file that stayed locked) |
| AmendNotAvailableException | `error.gitOperation.amendNotAvailable` | |
| OperationCancelledByUserException | `error.capture.cancelled` / `error.compaction.cancelled` / `error.launch.canceled` / `error.storageRelocation.cancelled` | `op`, `gameId?` |
| CompactionPreemptedException | `error.compaction.preempted` | `gameId?` |
| GameOverlapException | `error.gameService.pathOverlap` | `conflictingGameId`, `conflictingGameName` |
| ProfileActiveException | `error.gameService.profileActive` | `profileId` |
| ReconnectableGameException | `error.gameService.reconnectable` | `existingGameId`, `roles`, `branchNames` |
| GameRunningException | `error.gameService.gameRunning` | `gameId`, `gameName` |
| LaunchAlreadyRunningException | `error.launch.alreadyRunning` | `gameId` |
| NoActiveSessionException | `error.launch.noActiveSession` | `gameId` |
| SessionStillRunningException | `error.launch.sessionStillRunning` | `gameId` |
| DirtyWorkingTreeException | `error.launch.dirtyWorkingTree` | `role` |
| DetachedCaptureNotSupportedException | `error.launch.detachedCaptureNotSupported` | `role` |
| LaunchSpawnException | `error.launch.spawnFailed` | `method` (`LaunchMethod`) |
| LaunchWindowsProgramException | `error.launch.windowsProgram` | `path` |
| PathMissingException | `error.path.missing` | `path`, `role` |
| FolderChangedException | `error.path.changed` | `path`, `role` |
| SwitchNoteStuckException | `error.launch.switchNoteStuck` | `path` |
| RepositoryDamagedException | `error.gitOperation.recordDamaged` | |
| StorageDestinationNotEmptyException | `error.storageRelocation.destinationNotEmpty` | `path` |
| ValidationException | `error.validation.invalidPayload` | `issues` (list of `{ path, messageId }`) |
| CatalogCorruptException | `error.catalog.corrupt` | `path` |
| GitUnavailableException | `error.git.unavailable` | `path` |
| StorageStillReachableException | `error.storage.stillReachable` | `path` |
| StorageDefaultFolderHasHistoryException | `error.storage.defaultFolderHasHistory` | `path` |
| StorageFolderHasLinkException | `error.storage.folderHasLink` | `path` (the link) |
| (profile refusals) | `error.profile.notLoaded`, `error.removed.notFound`, `error.removed.notRestorable` | `profileId` or the removed snapshot |
| StorageWriteFailedException | `error.storage.writeFailed` | `path` |
| StorageUnreachableException | `error.storage.unreachable` | `path` |
| StorageHistoryNotFoundException | `error.storage.noHistoryHere` | `path` |
| StorageRemoveFailedException | `error.storage.removeFailed` | `path` |
| StorageSourceUnreadableException | `error.storageRelocation.sourceUnreadable` | `path` (the entry that cannot be read) |
| StorageInsideInstallFolderException | `error.storageRelocation.insideInstallFolder` | `path` (the chosen folder) |
| FileUnreadableException | `error.gitOperation.unreadableFile` | `path` |

More ids from D31: `error.gameService.folderMismatch`, `error.gameService.alreadyExists`, `error.gameService.noHistoryToReconnect`, `error.launch.completionNotOffered`, `error.storageRelocation.moveUnfinished`, `error.gitOperation.failed`. The XML docs of `src/Savepoint.Core/Errors/` are the authoritative list, including the ids that D28, D29 and the services add.

`ErrorInfo(string MessageId, IReadOnlyDictionary<string, object?> Args, string Detail)` is the only error shape that leaves Core. `ErrorInfo.From(Exception)` maps a `SavepointException` to its id and args (enum args become camelCase strings) and anything else to `error.unknown` with the exception text as `Detail`.

A user cancel surfaces as `OperationCanceledException`. The job registry and the Api facade map it to the matching cancelled id. A cancelled read returns `error.operation.cancelled` (D23).

## Platform

```csharp
public interface IPlatform
{
    OsKind Os { get; }                                // Windows, Linux, MacOS
    StringComparison PathComparison { get; }          // OrdinalIgnoreCase on Windows/macOS, Ordinal on Linux
    AppDirectories Directories { get; }               // CatalogDir, DefaultStorageRoot, LogsDir, HomeDir
    IReadOnlyDictionary<string, string> PathTokens { get; } // documents, appData, localAppData, userHome, savedGames (where it exists)
    PathDenylist Denylist(IEnumerable<string> extraBlockedRoots); // tier 1 (subtree) + tier 2 (exact) absolute paths
    IProcessInspector Processes { get; }              // list running processes (pid, name, executable path, parent pid and start time where the OS gives them)
    IGameLauncher Launcher { get; }                   // start an executable or app bundle; open a URI
    ISteamClient Steam { get; }                       // whether Steam runs and is signed in; start the client alone (D67)
}
```

One implementation per OS under `Platform/Windows`, `Platform/Linux`, `Platform/MacOS`. `PlatformFactory.ForCurrentOs()` picks one. Tests may construct any implementation with an injected home directory and environment, so all three tables are testable on any OS.

## Git

### The bundled git

`GitBinary` locates the bundled git under `<AppContext.BaseDirectory>/git/` and builds the base environment for the platform's bundle layout. Tests and dev builds override the location with `SAVEPOINT_GIT_ROOT`.
- With MinGit on Windows, it starts the real `git.exe` under `ucrt64/bin`, `mingw64/bin`, or `clangarm64/bin` directly, with the `PATH` and `GIT_EXEC_PATH` that `cmd/git.exe` would set. It uses `cmd/git.exe` only when no toolchain folder is found.
- With dugite-native elsewhere, it uses `bin/git` with `GIT_EXEC_PATH`, `GIT_TEMPLATE_DIR`, `GIT_CONFIG_SYSTEM`/`PREFIX`.
- `VerifyAsync()` runs `git --version` and throws `GitUnavailableException` when it cannot.

### Git never runs a program a repository names (D41.5, D42.1)

- Every git process gets `core.hooksPath` (an empty `no-hooks` folder in SavePoint's config folder), `core.fsmonitor=false`, `commit.gpgSign=false`, and `tag.gpgSign=false` through `GIT_CONFIG_COUNT`.
- SavePoint's config writes keep only the keys `git init` writes plus SavePoint's own block, and rewrite any other config text on the next touch.
- `AdoptedGitDirectory.MakeSafe` rewrites the config and empties the hooks folder of a git directory adopted through "Find the folder". A candidate that holds a link where SavePoint reads or writes (a game folder, a git directory, its `hooks`, `config`, or `info` entries) is refused with `error.storage.folderHasLink`.

### Running git

`GitRunner` runs one git process with an argument list (never a shell string), `GIT_DIR`/`GIT_WORK_TREE`, the pinned identity and config isolation (D13.24), and `GIT_OPTIONAL_LOCKS=0` for read-only calls. It also takes optional stdin, line-streamed stdout/stderr callbacks, or binary stdout. A non-zero exit throws `GitCommandException` (internal). The engine maps it to a typed error.

- **Cancel.** Cancellation kills the whole process tree (`ProcessTree.Kill`). Windows and macOS use the .NET call. On Linux, one call does the kill, and the exit-time kill runs it too. It stops the root, then reads `/proc` and stops each new descendant, pass after pass, until none appear. Then it sends SIGKILL to every member, deepest first and the root last.
- **Index repair.** `GitEngine.RunRawAsync` wraps each git call (`RunOnceAsync`): a call that fails with index damage, made by the game-lock holder (`GitEngine.ForGameLockHolder()`, handed out by `BackendServices.RunForGameAsync`), rebuilds the index beside the real one and runs once more (`GitEngine.IndexRepair.cs`, D58). Calls with their own `GIT_INDEX_FILE` or a streamed input are left alone.
- **Exit.** Every git the runner starts is listed in a `GitProcessRegistry`. A `ProcessExit` hook kills whatever is still running when SavePoint exits. On Windows each git except reclaim gits (`SurvivesExit`) also goes into a kill-on-close job object (`GitProcessJob`), and the registry writes a `running-git.json` note (`RunningGitNote`) that the next start reads through `LeftoverGits` (D59). A process stamp leaves a reused pid alone.

`GitEngine` holds v1's engine operations on a `RepoRef(string GitDir, string WorkTree, string GameId, RepoRole Role)`. Every operation that writes checks path safety and the consent marker first. A discard, clean or switch also pins the folder (`PathSafety.Pin`: real path, plus file identity from `HostPathProbe.ReadIdentity`, which reads the file ID on Windows and device and inode through `UnixFiles` elsewhere) through a `FolderGuard`: git runs on the pinned real path, every step that deletes, overwrites or creates files rechecks the pin and the marker right before it starts (`GitInvocation.BeforeStart` for git, the guard for .NET file steps, which also use the real path), and on Windows the folder is held open without delete sharing so it cannot be renamed or replaced (D57).

## Catalog

Models are immutable records with `System.Text.Json` source-generated serialization: `GameDefinition`, `ExternalLocation` (the saves folder), `ModsLocation` (a mods folder outside the install, D61), `LaunchConfig`, `Profile`, `AppSettings`. The JSON is camelCase and matches v1's shapes. Enums serialize as their v1 strings (`"exe"`, `"uri"`, `"saves"`, `"system"`, `"sidebar"`, `"blue"`, ...).

`CatalogStore` owns `games.json`, `profiles.json`, `settings.json` in `IPlatform.Directories.CatalogDir`. It has one async lock, an atomic write (temp file in the same directory, flush, `File.Move(overwrite: true)`), a `.bak` of the previous file, and `CatalogCorruptException` on unreadable content (D13.2).

## Services and the Api facade

The interfaces in `src/Savepoint.Core/Api/` are the contract between Core and the App. Services hold the behavior, and the facade is thin. View models depend only on the interfaces. `tests/Savepoint.App.Tests/Fakes/FakeBackend` implements all of them in memory (scripted jobs and sessions, a call log, one-shot failures, a scenario builder whose default is the mockup data), so screens are built and tested without the real services.

Rules that hold for every member:
- Every async call answers `OperationResult<T>`: `Ok` with a non-null `Value`, or `Error` (`ErrorInfo`). No exception crosses the boundary. A call with nothing to return answers `OperationResult<Unit>`. `OperationResult.Failure<T>(Exception)` maps through `ErrorInfo.From`, so the facade and the fake report an exception the same way.
- Reads take a `CancellationToken`. Long work starts a job and returns `JobStarted` at once. `IJobsApi.CancelAsync` cancels the job, so job starters take no token. Other mutations take no token either.
- `IJobsApi.JobChanged` and `ILaunchApi.SessionChanged` are raised on a background thread with the whole snapshot after each change. The App marshals them to the UI thread. Each snapshot has a `Revision` that grows with every change. A view model subscribes first, then reads the snapshot getters, and drops an event whose revision is not newer than the one it holds. A late subscriber thus never loses a result.
- Snapshot getters (`List`, `Get`, `GetSession`, `ListSessions`, `Templates`) are synchronous, read memory, and cannot fail, so they return plain values.

```csharp
public interface IGamesApi
{
    IReadOnlyList<GameTemplate> Templates { get; }
    Task<OperationResult<IReadOnlyList<GameDefinition>>> ListAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<AddGameOutcome>> AddAsync(GameDefinition definition, IReadOnlyList<string>? suppliedRules = null);  // Added(game) | HistoryFound(orphan); suppliedRules: the game-wide rules a template or definition file gave, the only ones the saves check may move (D7)
    Task<OperationResult<AddGameOutcome.Added>> ResolveOrphanAsync(GameDefinition definition, OrphanResolution resolution, IReadOnlyList<string>? suppliedRules = null);
    Task<OperationResult<GameDefinition>> UpdateAsync(GameDefinition definition, bool confirmFolderChange = false);  // re-points a moved folder; folderMismatch until confirmed (D31.4)
    Task<OperationResult<Unit>> DeleteAsync(string gameId);
    Task<OperationResult<IReadOnlyList<FolderStatus>>> GetFolderStatusAsync(string gameId, CancellationToken cancellationToken = default);
    Task<OperationResult<GameStorageUsage>> GetStorageUsageAsync(string gameId, CancellationToken cancellationToken = default);
    Task<OperationResult<CapturePreview>> GetCapturePreviewAsync(string gameId, CancellationToken cancellationToken = default);
    Task<OperationResult<JobStarted>> OptimizeStorageAsync(string gameId);
    Task<OperationResult<string>> ResolvePathAsync(string rawPath, CancellationToken cancellationToken = default);
    Task<OperationResult<IReadOnlyList<string>>> ListExecutablesAsync(string installPath, string gameName, CancellationToken cancellationToken = default);  // ranked like SuggestProcessNameAsync, at most 50
    Task<OperationResult<string>> SuggestProcessNameAsync(string installPath, string gameName, CancellationToken cancellationToken = default);  // the likely game program for a link start, "" when none (ProcessNameSuggester)
    Task<OperationResult<LibraryScan>> ScanLibrariesAsync(CancellationToken cancellationToken = default);   // installed Steam and GOG games, matched to templates, with "already added" (D52)
    Task<OperationResult<LibraryAddResult>> AddLibraryGameAsync(LibraryGame game);                            // one scanned game through AddAsync's checks: Added | AlreadyAdded | HasOlderHistory | Refused
    Task<OperationResult<GameDefinition>> ImportDefinitionAsync(string filePath, CancellationToken cancellationToken = default);  // never saves
    Task<OperationResult<Unit>> ExportDefinitionAsync(string gameId, string filePath);
    Task<OperationResult<bool>> FolderExistsAsync(string rawPath, CancellationToken cancellationToken = default);  // after tokens resolve; the editor leaves a template's missing mods folder out
}

public interface IProfilesApi
{
    Task<OperationResult<IReadOnlyList<Profile>>> ListAsync(string? gameId = null, CancellationToken cancellationToken = default);
    Task<OperationResult<IReadOnlyList<ProfileRowStats>>> GetRowStatsAsync(string gameId, CancellationToken cancellationToken = default);
    Task<OperationResult<JobStarted>> CreateAsync(string gameId, string name, string? notes, string pointName, IReadOnlyCollection<RepoRole>? startEmpty = null);  // wizard and keep-as-new; startEmpty: saves or mods start with an empty snapshot (D65)
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
    Task<OperationResult<JobStarted>> SwitchAsync(string gameId, LaunchTarget target);      // a Switch job: StartAsync's steps up to the start, then stops (D49)
    Task<OperationResult<Unit>> DiscardChangesAsync(string gameId, RepoRole folder);         // refused while a session is open or the game runs (D31.5)
    Task<OperationResult<Unit>> MarkFinishedAsync(string gameId);                            // "I'm done playing"
    Task<OperationResult<Unit>> CompleteSessionAsync(string gameId, SessionCompletion completion);  // only a choice the session offered
    Task<OperationResult<Unit>> RetryCompletionAsync(string gameId);                         // reruns a partial completion's plan under its capture id (D31.7)
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
    Task<OperationResult<StartupStatus>> CheckAsync(CancellationToken cancellationToken = default,
        IProgress<StartupProgress>? progress = null);   // reports FinishingEarlierCleanup while a leftover git ends
    Task<OperationResult<string>> RecoverCatalogFileAsync(string path, CatalogRecovery recovery);
    Task<OperationResult<string>> UseStorageFolderAsync(string folder);   // "Find the folder"
    Task<OperationResult<string>> StartWithEmptyStorageAsync();           // "Start with an empty folder"
}

public interface IArtworkApi
{
    event EventHandler<ArtworkChange>? ArtworkChanged;                                       // keyed by game id, after a background lookup
    Task<OperationResult<GameArtwork>> GetAsync(string gameId, CancellationToken cancellationToken = default);  // what is on disk now; never waits for a lookup
    Task<OperationResult<GameArtwork>> SetCustomImageAsync(string gameId, string imagePath);
    Task<OperationResult<GameArtwork>> ClearCustomImageAsync(string gameId);
    Task<OperationResult<GameArtwork>> LookAgainAsync(string gameId);
}
```

Catalog models (`GameDefinition`, `Profile`, `AppSettings`, `GameTemplate`, `RepoRole`) are reused as they are. `RepoRole` is `Install`, `External` (the saves folder) or `Mods`. Each has its own store (`install.git`, `external.git`, `mods.git`). The Api adds only what the catalog does not hold.

### Jobs

`JobKind` is `Launch` (prepare, switch, start the game), `Switch` (prepare and switch without starting the game, D49), `Capture`, `Compact` (the player's Free up space), `PackStorage` (the automatic tidy after a snapshot, D13.17), `RelocateStorage`, or `ClearData`. `JobStatus` is `Running`, `Succeeded`, `Partial`, `Failed`, or `Cancelled`.

`JobSnapshot` carries:
- the kind, the game (null for a job across every game), and the `JobProfile(Id, Name)` it is for, when there is one.
- the status, `Cancellable`, and `CancelRequested`. Every kind but `ClearData` is cancellable (D13.18). A job that reaches a point it cannot stop past calls `JobContext.NoLongerCancellable()`, as a storage move does after it switches over.
- `JobProgress { FilesDone, FilesTotal, BytesDone, BytesTotal, CurrentPath }`. Each report replaces the last.
- the newest 200 `RecentLines`, with `LinesDropped` (how many older lines fell off). `IJobsApi.GetLines(id)` returns up to `JobHistory.MaxLines` (20,000) of a job's newest lines for Copy (D50).
- the start and finish times, the `JobResult`, and the `ErrorInfo` of a failed or cancelled job.

A `JobLine` is a `FilePath` or a `Step` (a catalog message id and its arguments, from the constants of `JobSteps`). Text from git is never a line (D44.5).

`List()` keeps running jobs in start order, then each game's five most recently finished jobs, then the five most recently finished jobs that span every game. `JobRegistry` saves the finished ones to `activity-history.json` in the catalog folder as each ends and reads them back at start, without their steps (D44.5).

`StagingBatches` stages a watched capture in groups, and `ProgressReporter` estimates the big file git is on from the store's growth (`StoreMeter`) (D50).

Results by kind are `CaptureResult(ProfileId, PointId?, Folders)`, `OptimizeResult(Games)` with a `GameOptimizeOutcome(GameId, Folders, BytesFreed?)` per game, `StorageRelocationResult(PreviousRoot, NewRoot, BytesCopied)`, and `ClearAllDataResult(BytesFreed, NotRemoved)`. A launch job has none.

Folder work reports one `FolderOutcome(Role, Error?)` per folder it touched. All fine is `Succeeded`, all failed is `Failed` (or `Cancelled` when every failure is a cancellation), and anything between is `Partial` (D13.20). Two outcomes are neither saved nor failed:
- `FolderOutcome.LeftAlone`: a folder the launch left alone (D37.3). `LeftOnBranch` names the branch it sits on (null on an older point). `FolderStart` records each folder's commit and branch at launch.
- `FolderOutcome.Skipped`: an update found nothing unsaved in the folder. The result is still Ok.

### Launch

`LaunchTarget` is closed: `ProfileTip(ProfileId)`, `Save(ProfileId, SaveId)` for the dialog's save picker, and `Point(PointId)` for "Go back and play" (D15).

`LaunchPreview` holds:
- the resolved profile.
- `Folders`, one `FolderLaunchState` per folder the game has, in switch order (install, mods, saves); `Folder(role)` finds one and `Install` is always there. It reports:
  - whether the folder matches the target, whether it is dirty, how many files, and `DirtyNewFileCount` (D31.4).
  - the owning profile, or null when nothing can claim the changes.
  - a `SwitchSummary` of files, mod files, bytes written, bytes removed, files removed (`FilesRemoved`), whether the switch leaves the folder empty (`LeavesFolderEmpty`, which the skip setting never skips, D65), and an estimated duration, sized from the stored snapshot.
  - `HeldBy`, the held file that stands in the way of a switch (D37.5).
  - `UnfinishedSwitch` and its `UnfinishedSwitchTarget`, a `LaunchTarget` that "Switch again" starts (D31.6). For a stopped switch to the same target, `GitEngine.ReviewStoppedSwitchAsync` fills `ChangesOutsideSwitchCount` (changes that hold the finish back and withhold the target), `ReplacedFileCount` and `ReplacedFileNames` (saves folder, up to 5), and `StoppedSwitchUnchecked` when the review failed. The preview hashes candidate files to count them. Start and `CheckSwitchAsync` run only the cheap scan.
- the current and target `InstallState`: version and mod-file count, for "going from 1.6.1170 with no mods back to 1.6.640 with 189".
- the target profile's saves, newest first, with a most-recent marker and the selected one.
- the `ShowBanner`, `OnlySaveDiffers`, and `TargetIsOlderPoint` flags.

`InterruptedSwitch.ToBranch` records the branch a switch was for, so "Switch again" finishes it for the right profile.

`StartAsync` returns the launch job, which ends when the game runs. `IJobsApi.CancelAsync` on the launch job cancels the switch. The session then lives in `LaunchSessionInfo` (game, profile, target, `StartedFromOlderPoint`, launch job id, `Revision`). Its closed `SessionState` is one of:
- `Preparing`
- `Switching(progress)`
- `Running(startedAt)`, which carries the started program's exit code when it closes with one
- `Exited(startedAt, playDuration, newFileCount, changedInstallFileCount, options, endKind)`
- `Completing(completion, jobId)`
- `Completed(completion, result?, error?)`
- `Failed(error)`

`endKind` is `ChildExited`, `NamedProcessEnded`, `ManuallyFinished`, `NamedProcessNeverAppeared`, `NamedProcessNeverMatched`, `ProcessListUnreadable`, or `ProgramFailedAtStart` (D13.10). `ProgramFailedAtStart` carries the code in `programExitCode`.

`PostPlayOptions` lists the choices the session allows, the default, the point `UpdateExisting` would replace, and how many newer points leave the profile on `MoveForward` (D14). `ReplaceRule` checks D14's rule at exit and again inside the save.

`SessionCompletion` is `CaptureNew(pointName)`, `UpdateExisting(pointName)`, `ForkNewProfile(profileName, pointName)`, `MoveForward(pointName)`, or `Skip` (D14). Skip completes at once. Every other choice runs a capture job.

### Profiles

- `ProfileRowStats` holds the active marker, the folders with unsaved changes, the snapshot count (D44.3), the mod-file count, the detected version and override, last played, and running.
- `ProfileRowStats.SnapshotNow` (`Available`, `FirstSnapshot`, `NotLoaded`) says whether "Take a snapshot" applies. A capture of a profile that is not loaded fails with `error.profile.notLoaded`.
- `ProfileChanges` carries only the fields a user edits.
- `ProfileDeletePreview` counts the points only that profile holds, in total and per folder (D17). `BlockedBy` is the error the delete would fail with. `ProfileDeletePreview.PointsBackToRemoved` counts what goes back to Recently removed (D46.3).

### Games

- `AddGameOutcome` is `Added(game)` or `HistoryFound(OrphanedHistory)`.
- `Added.LeftOff` is `RulesLeftOff(Rules, SavesHoldFiles)` when the saves check moved game-wide default rules to the install-only list. `SavesHoldFiles` is false when the check could not run (D7). `ResolveOrphanAsync` returns the same `Added`, and `LibraryAddResult.RulesLeftOff` carries it for a library add.
- `GameDefinition` has `IgnorePaths` (every folder) and `InstallIgnorePaths` (the game's own folder only). Code that needs one folder's rules calls `IgnorePathsFor(role)`.
- `OrphanedHistory` names the old game id, the folders, and each old profile with a suggested name or null (the UI supplies fallback copy). `OrphanResolution` is `StartFresh` or `Reconnect(existingGameId, profiles)`, with the names the user will see.
- `FolderStatus` gives each folder's stored and resolved path, whether it exists (the "moved or is gone" card), and its `ConsentStatus(Confirmed, ConfirmedAt)`.
- `CapturePreview` lists each folder's path, file count, mod-file count, and bytes, plus the bytes a capture would add after reuse. Each folder also says whether its files are safe in a snapshot (`HeldInSnapshot`) and, when not, why (`StartEmptyBlocked`), which the new profile wizard needs for its start-empty boxes (D65).

### History

`GameHistory` lists `HistoryPoint`s newest first, optionally for one profile. Each point has its id, kind (`VersionModsAndSave`, `VersionAndMods`, `SaveOnly`, D15), name, date, profile id and name, the install state at that point, the mod files changed since the profile's previous install point, bytes where known, and the `InUse` and `LoadedNow` markers. A `VersionModsAndSave` point belongs in both columns of the list view. Points are ordered by the `Savepoint-Captured-At` trailer (D31.9).

### Switches and saves

- **Held files (D37.5, D41.1).** For a folder whose only changes are files git cannot read, `RepoStatus.IsClean` is true, `RepoStatus.HeldFile` names the first one, and `RepoStatus.HeldFileCount` counts them. `error.gitOperation.checkoutFailed` is a file in use, and `error.gitOperation.unreadableFile` a file that cannot be read at all.
- **Case-insensitive drives.** The clash check is case-insensitive on Windows and macOS, and wherever a probe of SavePoint's marker file shows that the folder's drive ignores case (NTFS or exFAT under Linux). There a switch, forced switch or discard refuses a point that holds two names that differ only in case (`Mods/Foo` and `Mods/foo`) before it writes anything (`error.launch.caseClash`).
- **Empty folders (D51).** Each snapshot lists its empty folders in a `Savepoint-Empty-Folders:` trailer.
- **First save of a folder (D37.2, D41.7).** `CaptureMode.Root` writes a profile's first point in a folder as a new branch with nothing behind it.

### Recently removed (D44.4, D45)

- Removed commits are recorded as refs under `refs/savepoint/removed/<ms>/<reason>/<branch>/<commit>`, outside `refs/heads` and `refs/tags`. Everything that lists profiles or points reads only `refs/heads`.
- `IHistoryApi.GetRecentlyRemovedAsync` lists a game's records, newest removal first.
- `IProfilesApi.BringBackAsync` makes a new profile from one record, or refuses with `error.removed.notRestorable`. When the record must be copied, it uses `GitEngine.CreateBranchAsCopyAsync`.
- `OptimizationMode.ReclaimClearingRemoved` clears the records, then reclaims.

### Free up space (D45, D46)

- `GetFreeUpSpacePreviewAsync` (on `IGamesApi` and `ISettingsApi`) returns a token that names the exact records it would delete. The optimize calls take that token. So does `EstimateFreeUpSpaceAsync` (on both APIs), which answers about how many stored bytes the run frees. It works through `GitEngine.EstimateFreedBytesAsync` and a cache keyed by a digest of the store's refs, HEAD and index (D66), and it refuses a token no preview issued.
- A run writes `savepoint-free-up.json` in the game's storage folder before it deletes, so the next start can finish it. After that, `NoLongerCancellable()` returns whether a stop was already accepted.
- A reclaim writes `savepoint-reclaim.json` in the git directory, naming SavePoint's process and the last git it started. The startup check and `RunForGameAsync` (every game write) wait for that git when its owner is gone.

### Storage and settings

- `GameStorageUsage` and `StorageSummary` give SavePoint's bytes per game and in total, and the storage volume's size and free space. The summary adds the last optimization date.
- `StorageSummary` and `StorageStatus` carry `InInstallerFolder`. It is true when the storage root lies inside the folder the Windows setup empties (D38). The startup warning and the Settings note read it.
- `StorageRelocationPreview` and `ClearAllDataPreview` list what will move or go, with sizes and a `BlockedBy` error.

A storage move (D31.3, D31.10, D37.4, D38) can fail with `error.storageRelocation.sourceUnreadable` (the entry in `path`), `error.storageRelocation.insideInstallFolder`, or `error.storageRelocation.moveUnfinished`. It writes `savepoint-move-in-progress.txt` into the destination while the copy runs. `StorageRelocationResult.NotRemoved` lists the old copies it could not remove, and the job ends Partial when that list is not empty. `error.storage.removeFailed` is an old copy that closing a program will not free.

### Startup (D18, D37.1, D41.4 to D41.6)

`StartupStatus` holds `GitStatus(Available, Path, Version, Error)`, `CatalogStatus(Directory, CorruptFiles)`, and `StorageStatus(Reachable, Root, Error)`. Each `CorruptCatalogFile` names its path, its last good backup if any, and the error. `RecoverCatalogFileAsync` restores the backup or starts that file empty, and answers with where it moved the corrupt file (D13.2).

When the storage folder cannot be reached:
- `UseStorageFolderAsync(folder)` ("Find the folder") fails with `error.storage.noHistoryHere` unless the folder holds SavePoint's history for at least one listed game.
- `StartWithEmptyStorageAsync` ("Start with an empty folder") fails with `error.storage.defaultFolderHasHistory` when a stamped git directory in the default folder has a branch.
- Both fail with `error.storage.stillReachable` while the saved folder can be reached, and with `error.storage.writeFailed`, which can be retried, when a write fails. Both create every listed game's repositories with `InitAsync`.

### Diagnostic log (D34)

Core writes through `IDiagnosticLog`, which `BackendOptions.Log` supplies. The default writes nothing.

The App's log is `Hosting/SerilogDiagnosticLog`. It writes `savepoint-<date>.log` into `AppDirectories.LogsDir`:
- Windows: `%LOCALAPPDATA%\SavePoint Data\Logs`. `AppDirectories.LegacyLocalDir` names the old `%LOCALAPPDATA%\SavePoint`, which the one-time move at startup empties (D38).
- macOS: `~/Library/Logs/SavePoint`
- Linux: `~/.local/state/savepoint/logs`

`AppServices.LogFolder` carries the folder to Settings > About and to the startup screen. The crash handler lives in `Hosting/` and exits with code 1.

### Artwork (D30)

`IArtworkApi` answers at once with what is on disk. A missing image starts a background lookup, and the `ArtworkChanged` event (keyed by game id) delivers the result. The event fires only when the kept images change, so a lookup that finds the same bytes again raises nothing.
- **Images** live in `<catalog dir>/artwork/<gameId>/`: `custom.<ext>`, `steam-wide.jpg`, `steam-square.jpg`, and `lookup.json`, which records the app id used and a miss.
- **Online lookup** runs only when `BackendOptions.ArtworkHttp` is set, and only `AppBootstrap` supplies the real handler. `BackendOptions.SteamFolder` lets tests point at a fake Steam folder.
- **Retries:** a lookup that comes to an answer runs at most once per run for each game, app id, and online setting. A lookup that fails for want of a connection is not remembered, and is tried again at the next read.
- **The image check** (`ImageProbe`) accepts PNG, JPEG, and WebP up to 25 MB, 32,767 px per side, and 100 megapixels.
- **Errors:** `error.artwork.notAnImage` (`path`) and `error.artwork.tooLarge` (`path`, `maxMegabytes`). An invalid app id is `validation.game.steamAppIdInvalid`.

## App

- `Hosting/AppBootstrap` is the composition root. `Hosting/AppBootstrap.Start` builds Core against the real platform through `CoreBackend.Create` with the launch orchestrator. `BackendOptions.Launch` is required. `AppComposition` then wires the view models to the Api it returns, with one `Composition/FeatureComposition.<Feature>.cs` file per feature.
- `AppServices.BackendStarted` is false when the backend failed to start. The startup screen then says to restart SavePoint.
- `ShellViewModel` owns navigation (Games, History, Settings), layout, and the selected game. The shell, not a screen, owns `LaunchFlowViewModel` (D13.19).
- `ExitGuard` asks before quitting while a job runs that should not stop (D31.11, D45.3).
- `Localization/` holds `LocalizationCatalog` (ICU formatting through MessageFormat), the `{loc:Loc}` markup extension, and `L.Format(key, args)` for view models. Message ids from `ErrorInfo` are catalog keys.
- `Theme/` holds the Material color port (`Theme/Material/`), `ThemeService` (seed + mode -> resource dictionary of MD3 roles, live switching), styles and control themes keyed to role names, and fonts under `Assets/Fonts`.
- `Platform/` holds OS integration that needs the UI stack (notifications, folder reveal, single instance, and `FolderWatching/`: `IFolderWatcher`, which the Games screen uses to follow the shown game's folders, D68; `AppServices.FolderWatcher` carries it). A screen hears it is left through `IScreen.OnHidden`.

`SingleInstance` lets one copy run per user. It holds a named mutex and an exclusive lock on a file beside its socket, and listens on a named pipe so a second copy can bring its window forward and exit (D26, D39).
