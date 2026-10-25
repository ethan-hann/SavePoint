# SavePoint v2 - architecture

How the code is organized and what each layer promises the one above it. The behavior spec was the unpublished v1's `plan.md` and inventories of its code; every place v2 departs from them is in `docs/decisions.md`. Where they disagree, this file and `decisions.md` win.

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

Rules for the layers:
- Views bind only to view models. View models call only the Api interfaces and `IDialogs` (folder pickers, file pickers, confirmations). Nothing in `Savepoint.App` references `Git/`, `Paths/`, or `Services/` types directly; `Hosting/AppBootstrap` reaches them only through `CoreBackend.Create` and the launch orchestrator.
- `Savepoint.Core` has no Avalonia reference and no static mutable state. Everything is constructed at startup and injected, so tests build the same graph against temp directories. The composition root is `Hosting/AppBootstrap`, which builds Core through `CoreBackend.Create`; `AppComposition` then wires the view models to the Api it returns.
- Every file-system or process side effect in Core goes through an injected abstraction where tests need to fake it (`IPlatform`, `IClock`, `IProcessInspector`, `IGameLauncher`); plain temp-dir IO is used everywhere else.
- Async all the way: every operation that touches disk or git is `async Task` and takes a `CancellationToken`.

## Errors

`SavepointException` (abstract) carries `MessageId` (a catalog key such as `error.pathSafety.tier1Blocked`), `Args` (`IReadOnlyDictionary<string, object?>` for ICU placeholders and extras such as `path`, `role`, `conflictingGameId`), and the English `Message` (dev-facing, logged, never shown; the error card's "Copy details for support" copies it with git's output for a bug report, D48.3).

Subclasses and ids (v1's ids are kept, plus the new ones marked *new*):

| Exception | MessageId | Args |
|---|---|---|
| PathSafetyException | `error.pathSafety.tier1Blocked` / `tier2Blocked` | `path`, `tier` |
| ConsentMarkerException | `error.consentMarker.missing` / `conflict` | `reason`, `path`, `role`, `existingGameId?`, `existingRole?` |
| DiskSpaceException | `error.diskSpace.low` | `path`, `requiredBytes`, `freeBytes` |
| GitOperationException | `error.gitOperation.checkoutFailed` | `path?` (the file that stayed locked) |
| AmendNotAvailableException | `error.gitOperation.amendNotAvailable` | |
| OperationCancelledByUserException | `error.capture.cancelled` / `error.compaction.cancelled` / `error.launch.canceled` / `error.storageRelocation.cancelled` *new* | `op`, `gameId?` |
| CompactionPreemptedException *new* | `error.compaction.preempted` | `gameId?` |
| GameOverlapException | `error.gameService.pathOverlap` | `conflictingGameId`, `conflictingGameName` |
| ProfileActiveException | `error.gameService.profileActive` | `profileId` |
| ReconnectableGameException | `error.gameService.reconnectable` | `existingGameId`, `roles`, `branchNames` |
| GameRunningException *new* | `error.gameService.gameRunning` | `gameId`, `gameName` |
| LaunchAlreadyRunningException | `error.launch.alreadyRunning` | `gameId` |
| NoActiveSessionException | `error.launch.noActiveSession` | `gameId` |
| SessionStillRunningException | `error.launch.sessionStillRunning` | `gameId` |
| DirtyWorkingTreeException | `error.launch.dirtyWorkingTree` | `role` |
| DetachedCaptureNotSupportedException | `error.launch.detachedCaptureNotSupported` | `role` |
| LaunchSpawnException | `error.launch.spawnFailed` | `method` (`LaunchMethod`) |
| LaunchWindowsProgramException *new* | `error.launch.windowsProgram` | `path` |
| PathMissingException | `error.path.missing` | `path`, `role` |
| StorageDestinationNotEmptyException | `error.storageRelocation.destinationNotEmpty` | `path` |
| ValidationException | `error.validation.invalidPayload` | `issues` (list of `{ path, messageId }`) |
| CatalogCorruptException *new* | `error.catalog.corrupt` | `path` |
| GitUnavailableException *new* | `error.git.unavailable` | `path` |
| StorageStillReachableException *new* | `error.storage.stillReachable` | `path` |
| StorageDefaultFolderHasHistoryException *new* | `error.storage.defaultFolderHasHistory` | `path` |
| StorageFolderHasLinkException *new* | `error.storage.folderHasLink` | `path` (the link) |
| (profile refusals) *new* | `error.profile.notLoaded`, `error.removed.notFound`, `error.removed.notRestorable` | `profileId` or the removed snapshot |
| StorageWriteFailedException *new* | `error.storage.writeFailed` | `path` |
| StorageUnreachableException *new* | `error.storage.unreachable` | `path` |
| StorageHistoryNotFoundException *new* | `error.storage.noHistoryHere` | `path` |
| StorageRemoveFailedException *new* | `error.storage.removeFailed` | `path` |
| StorageSourceUnreadableException *new* | `error.storageRelocation.sourceUnreadable` | `path` (the entry that cannot be read) |
| StorageInsideInstallFolderException *new* | `error.storageRelocation.insideInstallFolder` | `path` (the chosen folder) |
| FileUnreadableException *new* | `error.gitOperation.unreadableFile` | `path` |

`ErrorInfo(string MessageId, IReadOnlyDictionary<string, object?> Args, string Detail)` is the only error shape that leaves Core. `ErrorInfo.From(Exception)` maps a `SavepointException` to its id and args (enum args become camelCase strings) and anything else to `error.unknown` with the exception text as `Detail`.

A user cancel surfaces as `OperationCanceledException`; the job registry and the Api facade map it to the matching cancelled id themselves (cancelled reads return `error.operation.cancelled`, D23). Further ids added by later decisions (D28, D29, and the services) are listed in the XML docs of `src/Savepoint.Core/Errors/`, which are the authoritative list.

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

- `GitBinary`: locates the bundled git under `<AppContext.BaseDirectory>/git/` (override: `SAVEPOINT_GIT_ROOT` for tests and dev) and builds the base environment for the platform's bundle layout. With MinGit on Windows, it starts the real `git.exe` under `ucrt64/bin`, `mingw64/bin`, or `clangarm64/bin` directly, with the `PATH` and `GIT_EXEC_PATH` that `cmd/git.exe` would set, which saves one process per git call; it uses `cmd/git.exe` only when no toolchain folder is found. With dugite-native elsewhere, it uses `bin/git` with `GIT_EXEC_PATH`, `GIT_TEMPLATE_DIR`, `GIT_CONFIG_SYSTEM`/`PREFIX`. `VerifyAsync()` runs `git --version` and throws `GitUnavailableException` when it cannot.
- **Git never runs a program a repository names (D41.5, D42.1).** Every git process gets four settings through `GIT_CONFIG_COUNT`, which outranks a repository's own config: `core.hooksPath` (an empty `no-hooks` folder in SavePoint's config folder), `core.fsmonitor=false`, `commit.gpgSign=false`, and `tag.gpgSign=false`. SavePoint's config writes keep only the keys `git init` writes plus SavePoint's own block. A config counts as current only when its text is exactly what SavePoint would write, so a foreign key or a changed value in the managed block is rewritten on the next touch whatever the stamp. A git directory adopted through "Find the folder" goes through `AdoptedGitDirectory.MakeSafe` (config rewritten, hooks folder emptied) before anything else reads it. A candidate that holds a link anywhere SavePoint would read or write (a game folder, a git directory, its `hooks`, `config`, or `info` entries) or a path that resolves outside it is refused whole with `error.storage.folderHasLink` before anything is written.
- `GitRunner`: runs one git process with an argument list (never a shell string), `GIT_DIR`/`GIT_WORK_TREE`, the pinned identity and config isolation (D13.24), `GIT_OPTIONAL_LOCKS=0` for read-only calls, optional stdin, line-streamed stdout/stderr callbacks, or binary stdout. Cancellation kills the whole process tree (`Process.Kill(entireProcessTree: true)`). Non-zero exit throws `GitCommandException` (internal; the engine maps it to a typed error).
- `GitEngine`: v1's engine operations on a `RepoRef(string GitDir, string WorkTree, string GameId, RepoRole Role)`. Every operation that writes checks path safety and the consent marker first.

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
    Task<OperationResult<string>> RecoverCatalogFileAsync(string path, CatalogRecovery recovery, CancellationToken cancellationToken = default);
    Task<OperationResult<string>> UseStorageFolderAsync(string folder, CancellationToken cancellationToken = default);   // "Find the folder"
    Task<OperationResult<string>> StartWithEmptyStorageAsync(CancellationToken cancellationToken = default);              // "Start with an empty folder"
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

Catalog models (`GameDefinition`, `Profile`, `AppSettings`, `GameTemplate`, `RepoRole`) are reused as they are; the Api adds only what the catalog does not hold.

**Jobs.** `JobKind`: `Launch` (prepare, switch, start the game), `Capture`, `Compact`, `RelocateStorage`, `ClearData`. `JobStatus`: `Running`, `Succeeded`, `Partial`, `Failed`, `Cancelled`. `JobSnapshot` carries the kind, the game (null for jobs across every game), the `JobProfile(Id, Name)` it is for when there is one, status, `Cancellable` (every kind but `ClearData`, D13.18; a job that reaches a point it cannot stop past calls `JobContext.NoLongerCancellable()`, as a storage move does once it has switched over), `CancelRequested`, `JobProgress { FilesDone, FilesTotal, BytesDone, BytesTotal, CurrentPath }` (each report replaces the last), the newest 200 `RecentLines` with `LinesDropped` (how many older lines fell off, so a reader can tell which are new), start and finish times, the `JobResult`, and the `ErrorInfo` of a failed or cancelled job. A watched capture stages in groups (`StagingBatches`: files under 128 MiB in groups of at most 2 GiB or 2000, then every bigger file in one last add so it writes one pack, D50), and its names reach `RecentLines` as each group ends; during the last add, `ProgressReporter` estimates the big file git is on from the store's growth (`StoreMeter`).

A `JobLine` is closed to two shapes so no free text reaches a player: a `FilePath`, shown as it is, or a `Step` with a catalog message id and its arguments, which the App formats (the ids Core writes are the constants of `JobSteps`; text from git is never a line). Every job kind writes steps.

`List()` keeps running jobs in start order, then, for each game, its five most recently finished, and the five most recently finished that span every game, so a busy game never pushes another game's jobs out.

Results by kind: `CaptureResult(ProfileId, PointId?, Folders)`, `OptimizeResult(Games)` with a `GameOptimizeOutcome(GameId, Folders, BytesFreed?)` per game, `StorageRelocationResult(PreviousRoot, NewRoot, BytesCopied)`, `ClearAllDataResult(BytesFreed, NotRemoved)`; a launch job has none. Folder work reports one `FolderOutcome(Role, Error?)` per folder it touched: all fine is `Succeeded`, all failed is `Failed` (or `Cancelled` when every failure is a cancellation), anything between is `Partial`, never success (D13.20).

**Launch.** `LaunchTarget` is closed: `ProfileTip(ProfileId)`, `Save(ProfileId, SaveId)` for the dialog's save picker, and `Point(PointId)` for "Go back to this" (D15), which therefore gets the same preview and dirty checks as Play. `LaunchPreview` holds the resolved profile, a `FolderLaunchState` per folder (matches target, dirty and how many files, the owning profile or null when nothing can claim the changes, and a `SwitchSummary` of files, mod files, bytes written, bytes removed (sized from the stored snapshot, never by reading the folder), and an estimated duration), the current and target `InstallState` (version and mod-file count, for "going from 1.6.1170 with no mods back to 1.6.640 with 189"), the target profile's saves newest first with a most-recent marker and the selected one, and the `ShowBanner`, `OnlySaveDiffers`, and `TargetIsOlderPoint` flags.

`StartAsync` returns the launch job, which ends once the game runs; the session then lives in `LaunchSessionInfo` (game, profile, target, `StartedFromOlderPoint`, launch job id, `Revision`) whose closed `SessionState` is `Preparing`, `Switching(progress)`, `Running(startedAt)`, `Exited(startedAt, playDuration, newFileCount, changedInstallFileCount, options, endKind)` (`endKind` says why the session ended: `ChildExited`, `NamedProcessEnded`, `ManuallyFinished`, `NamedProcessNeverAppeared` when nothing SavePoint could see ran and the process name never matched, so `playDuration` is only the wait and is not shown as play time; `NamedProcessNeverMatched` when the started program ran but the name never matched, so the session ends at that program's exit; `ProcessListUnreadable` when the running programs could not be read during the grace), `Completing(completion, jobId)`, `Completed(completion, result?, error?)`, or `Failed(error)`. Cancelling the switch is `IJobsApi.CancelAsync` on the launch job.

`PostPlayOptions` lists the choices the session allows, the default, the point `UpdateExisting` would replace, and how many newer points leave the profile on `MoveForward` (D14's second confirmation). `SessionCompletion` is `CaptureNew(pointName)`, `UpdateExisting(pointName)`, `ForkNewProfile(profileName, pointName)`, `MoveForward(pointName)`, `Skip`; the point name is D14's message. Skip completes at once; every other choice runs a capture job. A finished or failed session stays visible until the next launch of that game.

**Profiles.** `ProfileRowStats`: active marker, folders with unsaved changes, the snapshot count (every snapshot the profile owns, counted with History's ownership rule, so a duplicate that has taken none of its own reads 0; D44.3), mod-file count, detected version and override, last played, running. `ProfileChanges` carries only the fields a user edits. `ProfileDeletePreview` counts the points only that profile holds, in total and per folder (D17), and carries `BlockedBy`, the error the delete would fail with (active profile, running game).

**Games.** `AddGameOutcome` is `Added(game)` or `HistoryFound(OrphanedHistory)`: finding old history is a question for the user, not an error. `OrphanedHistory` names the old game id, the folders, and each old profile with a suggested name or null (the UI supplies fallback copy); `OrphanResolution` is `StartFresh` or `Reconnect(existingGameId, profiles)` with the names the user will see. `FolderStatus` gives each folder's stored and resolved path, whether it exists (the "moved or is gone" card), and its `ConsentStatus(Confirmed, ConfirmedAt)`. `CapturePreview` lists each folder's path, file count, mod-file count, and bytes, plus the bytes a capture would add after reuse.

**History.** `GameHistory` lists `HistoryPoint`s newest first, optionally for one profile: id, kind (`VersionModsAndSave`, `VersionAndMods`, `SaveOnly`, D15), name, date, profile id and name, the install state at that point, mod files changed since the profile's previous install point, bytes where known, and the `InUse` and `LoadedNow` markers. A `VersionModsAndSave` point belongs in both columns of the list view.

**Storage and settings.** `GameStorageUsage` and `StorageSummary` give SavePoint's bytes per game and in total and the storage volume's size and free space; the summary adds the last optimization date. A storage move carries everything in the storage root, including a readable folder that is not a game's (such as "game - Copy"), and removes it from the old root with the rest; only a folder that cannot be opened and is not named like a game folder is left where it is. `StorageRelocationPreview` and `ClearAllDataPreview` list what will move or go, with sizes and a `BlockedBy` error, so the confirmation is exact. After a move is verified and SavePoint has switched to the new root, the old copies are deleted; a copy that cannot be removed is named in the job's lines and left in place.

**Startup (D18, D37).** `StartupStatus` holds `GitStatus(Available, Path, Version, Error)`, `CatalogStatus(Directory, CorruptFiles)`, and `StorageStatus(Reachable, Root, Error)`. Each `CorruptCatalogFile` names its path, its last good backup if any, and the error. `RecoverCatalogFileAsync` restores the backup or starts that file empty; either way the corrupt file is moved aside, never deleted, and the call answers with where it went. When the storage folder cannot be reached, the startup screen offers three ways on:
- check again, after the drive is back;
- `UseStorageFolderAsync(folder)`, "Find the folder": a settings change that copies nothing, refused with `error.storage.noHistoryHere` unless the folder holds SavePoint's history for at least one listed game;
- `StartWithEmptyStorageAsync`, "Start with an empty folder": starts over in the platform's default storage folder, keeps games and profiles listed with no history, and leaves the old folder untouched; refused with `error.storage.defaultFolderHasHistory` when the default folder already holds history.

Both choices refuse with `error.storage.stillReachable` while the saved folder can be reached. When a choice fails, the startup screen shows the message and, under it, the folder the error names, on the same surface the error card uses for a path.

"Find the folder" also runs the checks a move runs on its destination (the installer-owned folder, an unfinished move's marker, overlap with game folders) and makes each adopted git directory safe (D41.5, D41.6).

After either choice, every listed game gets its repositories at once (`InitAsync` for each folder whose consent marker names the game), so a profile starts its history again with its next save (D41.4). A folder that cannot be confirmed then is skipped with a log line, and its first save makes the repository.

Each choice checks again that the storage is lost once it holds the exclusive gate, so a second choice queued behind the first cannot replace the root the first one found. An empty start counts the default folder as holding history only when a stamped git directory there has a branch, so a start that failed part way can be made again, and a write that fails while making the repositories is `error.storage.writeFailed`, which can be retried.

**From the backend review (D31).**
- **Launch preview:** each `FolderLaunchState` also reports `DirtyNewFileCount` (so the discard confirmation can count what it removes) and `UnfinishedSwitch` with its `UnfinishedSwitchTarget`, a `LaunchTarget` that "Switch again" starts to finish the switch.
- **Capture times:** commits carry a `Savepoint-Captured-At` trailer (milliseconds), and History and the launch resolver order points by it, using one shared rule.
- **Storage move:** `StorageRelocationResult.NotRemoved` lists the old copies left behind, and the job ends Partial when it is not empty. While the copy runs, the destination holds `savepoint-move-in-progress.txt`, and a later move refuses a destination that still has it (`error.storageRelocation.moveUnfinished`).
- **New error ids:** `error.gameService.folderMismatch`, `error.gameService.alreadyExists`, `error.gameService.noHistoryToReconnect`, `error.launch.completionNotOffered`, `error.storageRelocation.moveUnfinished`, `error.gitOperation.failed`. `error.launch.unavailable` is gone, and `BackendOptions.Launch` is required.
- **App:** `AppServices.BackendStarted` is false when the backend failed to start; the startup screen then says to restart SavePoint. `ExitGuard` asks before quitting during a clear-all or a storage move, cancels the jobs that can be canceled, and waits up to three seconds.

**From the recheck (D37).**
- **Held files:** a file git cannot read (another program holds it, or access is denied) no longer counts as a change for a switch. When every change git lists is such a file, `RepoStatus.IsClean` is true, `RepoStatus.HeldFile` names the first one, and `RepoStatus.HeldFileCount` counts them. The launch preview reports it in `FolderLaunchState.HeldBy`, so the dialog names the file that stands in the way of a switch and asks to try again instead of offering to keep or discard anything. `error.gitOperation.checkoutFailed` covers a file in use and `error.gitOperation.unreadableFile` a file that cannot be read at all.
- **Finishing a switch:** `InterruptedSwitch.ToBranch` records the branch a switch was for, because profiles can share a point (a duplicate starts at its source's), so "Switch again" finishes the switch for the right profile. A half-switched folder counts the files git lists.
- **Saving with a held file (D41.1):** a save never skips such a folder as clean. It runs, fails with the held-file error that names the file, and the existing retry applies, so an unchanged file another program holds also fails an update until it is let go. The after-play dialog counts held files.
- **Recently removed (D44.4):** every commit a move forward or an amend takes off a branch is recorded first, as a ref outside `refs/heads` and `refs/tags` (`refs/savepoint/removed/<ms>/<reason>/<branch>/<commit>`), and only then is the branch moved. So a stop between the two leaves a harmless record for a commit its branch still reaches (the listing skips those) and never a commit that is neither on its branch nor recorded. Branch listings, History, the delete-profile warning, the row count, and the launch resolver read only `refs/heads` and never see them. `IHistoryApi.GetRecentlyRemovedAsync` lists a game's records, newest removal first; `IProfilesApi.BringBackAsync` makes a new profile from one, creating every branch in both folders or none (a folder the snapshot did not record starts at the origin profile's latest commit at or before it; with no install to start from it refuses with `error.removed.notRestorable`). Only Free up space clears the records: `OptimizationMode.ReclaimClearingRemoved` deletes them in one transaction, then reclaims, and `GetFreeUpSpacePreviewAsync` (on `IGamesApi` and `ISettingsApi`) says how many it would delete; the automatic reclaim after a delete keeps them. `ProfileRowStats.SnapshotNow` (`Available`, `FirstSnapshot`, `NotLoaded`) says whether "Take a snapshot now" applies; a profile that is not loaded is refused with `error.profile.notLoaded` before a job is queued.
- **Free up space as built (D45, D46):** `GetFreeUpSpacePreviewAsync` returns a token naming the exact records (each with the value the preview read), and the optimize calls take it; the run deletes only those, keeps every record of a snapshot that gained one after the preview, and also deletes a branch no profile names when every snapshot it alone holds is being deleted (never HEAD's branch or one a profile names; an unreadable HEAD offers none). It checks every folder first, writes `savepoint-free-up.json` in the game's storage folder naming every ref in both folders, deletes them, and from then on cannot be canceled (`NoLongerCancellable()` returns whether a stop was already accepted). Startup and every later free-up finish a note they find, and until then the listing, preview, and Bring back treat its refs as gone. A reclaim writes `savepoint-reclaim.json` in the git directory naming SavePoint's process and the last git it started; the startup check and `RunForGameAsync` (every game write) wait for that git when its owner is gone and it still runs, matched by start time. `ProfileDeletePreview.PointsBackToRemoved` counts what goes back to Recently removed, judging each folder's commit on its own.
- **Exit guard:** besides clear-all and a storage move, it asks before quitting during a switch (the folder is left with a mix of two snapshots), and for a free-up past its point of no return it offers only "Quit when it's done" and waits.
- **Case-insensitive drives:** the clash check picks its comparison per work tree, case-insensitive on Windows and macOS and wherever a probe shows the drive ignores case (NTFS or exFAT under Linux; the probe asks about SavePoint's marker file inside the folder first, so a folder that is itself a mount root is judged by its own drive). On such a drive a point holding two names that differ only in case (`Mods/Foo` and `Mods/foo`) is refused before a switch, forced switch or discard writes anything (`error.launch.caseClash`).
- **Files the ignore list names (D41.2):** a switch leaves them exactly as they are on disk, whether or not a point recorded them, and never refuses because of one; a save stops recording them (earlier points keep them); a discard leaves them too.
- **Empty folders (D51):** git tracks files only, so each snapshot records the game folder's empty folders in its own commit message, as a `Savepoint-Empty-Folders:` trailer (a one-line JSON array of the innermost empty folders, filtered by the exclude list). A switch, go back, forced switch, finishing a stopped switch, discard and clean then create any listed folder that is missing. They only create; a switch never removes an empty folder the target lacks beyond what git itself prunes. Snapshots without the trailer behave as before.
- **Folders the launch left alone:** a save reports such a folder as `FolderOutcome.LeftAlone`, with `LeftOnBranch` naming the branch it sits on (null on an older point), instead of counting it as saved. `FolderStart` records each folder's commit and branch at launch (D37.3).
- **Folders with nothing new:** an update that finds nothing unsaved in a folder reports `FolderOutcome.Skipped` (still Ok, not Saved), and the partial card shows that folder as kept as it was rather than saved or failed.
- **First save of a folder:** `CaptureMode.Root` starts a new branch whose first point has nothing behind it, holding what the folder holds now, from whatever branch the folder is on. Where the repository already has a point, it writes one even when nothing changed; on a repository with none it is a normal capture, and an empty folder gets an empty branch instead. A profile that saves a folder for the first time uses it, so its history there starts on its own instead of continuing another profile's.
- **Storage moves:** a move refuses a destination inside the installer's own folder (`error.storageRelocation.insideInstallFolder`, D38), and refuses to start when anything in the current storage cannot be read (`error.storageRelocation.sourceUnreadable`, named in `path`). A folder that cannot be opened and is not named like a game folder does not stop the move and is left in place. An old copy that cannot be deleted for a reason closing a program will not fix is `error.storage.removeFailed`.

**Diagnostic log (D34).** Core writes through `IDiagnosticLog`, which `BackendOptions.Log` supplies; the default writes nothing, and the backend wraps it so that a failing log can never throw through the Api. The App's log is `Hosting/SerilogDiagnosticLog`. It writes `savepoint-<date>.log` into `AppDirectories.LogsDir`:
- Windows: `%LOCALAPPDATA%\SavePoint\Logs`;
- macOS: `~/Library/Logs/SavePoint`;
- Linux: `~/.local/state/savepoint/logs`.

`AppServices.LogFolder` carries the folder to Settings > About and to the startup screen. The crash handler in `Hosting/` catches unhandled exceptions from the UI thread, the domain, and unobserved tasks, logs them, shows the last window, and exits with code 1.

**Artwork (D30).** `IArtworkApi` answers with what is on disk at once and never waits for a lookup. A missing image starts a background lookup, and the `ArtworkChanged` event (keyed by game id) delivers the result.
- **Images** live in `<catalog dir>/artwork/<gameId>/`: `custom.<ext>` (the user's own, copied in), `steam-wide.jpg`, `steam-square.jpg`, and `lookup.json`, which records the app id used and a miss for 7 days. A missing app id counts as a miss.
- **Sources:** the local Steam cache first, then Steam's CDN, but only when `BackendOptions.ArtworkHttp` is set. Without it, online lookup is off, which keeps every test offline; only `AppBootstrap` supplies the real handler, which follows no redirects (any 3xx is a failure) and sends no cookies. `BackendOptions.SteamFolder` lets tests point at a fake Steam folder.
- **Retries:** a lookup that comes to an answer (found, not found, or no id) runs at most once per run for each combination of game, app id, and online setting. A lookup that fails for want of a connection is remembered neither in the run nor across a restart: it is tried again at the next read, logged once, and art already kept stays. "Look again", a changed app id, or turning the setting back on retries sooner.
- **The image check** (`ImageProbe`) accepts PNG, JPEG, and WebP up to 25 MB, 32,767 px per side, and 100 megapixels.
- **Errors:** `error.artwork.notAnImage` (`path`) and `error.artwork.tooLarge` (`path`, `maxMegabytes`); an invalid app id is `validation.game.steamAppIdInvalid`.
- **Cleanup:** deleting a game removes its artwork folder, and clear-all removes them all; a storage move leaves them alone.

## App

- `Hosting/AppBootstrap.Start` builds Core against the real platform (`CoreBackend.Create` with the launch orchestrator) and hands the Api implementations to the App; `Composition/FeatureComposition.<Feature>.cs` wires them into view models, one file per feature.
- `ShellViewModel` owns navigation (Games, History, Settings), layout, and the selected game. `LaunchFlowViewModel` is owned by the shell, not a screen (D13.19).
- `Localization/`: `LocalizationCatalog` (ICU formatting via MessageFormat), `{loc:Loc}` markup extension, `L.Format(key, args)` for view models. Message ids from `ErrorInfo` are catalog keys.
- `Theme/`: Material color port (`Theme/Material/`), `ThemeService` (seed + mode -> resource dictionary of MD3 roles, live switching), styles and control themes keyed to role names, fonts under `Assets/Fonts`.
- `Platform/`: OS integration that needs the UI stack (notifications, folder reveal, single instance). `SingleInstance` lets one copy run per user. The running copy holds a named mutex and an exclusive lock on a file beside its socket, and listens on a named pipe so a second copy can bring its window forward and exit. Either guard alone keeps a second copy out, which covers a copy the machine gives no mutex (another session, a sandbox). Where the machine refuses the mutex, the file alone decides; where the file cannot be written, the mutex alone decides. A machine that allows neither keeps every copy from running, because two copies on the same repositories would remove each other's git locks as stale. The lock belongs to the open handle, so a crashed copy blocks nobody.
