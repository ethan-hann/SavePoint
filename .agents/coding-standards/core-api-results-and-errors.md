---
paths:
  - "src/Savepoint.Core/**/*.cs"
  - "src/Savepoint.App/**/*.cs"
---

# Api results and errors

- **Status:** accepted
- **Date Created:** 2026-10-26 17:15
- **Last Updated:** 2026-10-26 18:10
- **Applies To:**
  - Any member of an interface in `src/Savepoint.Core/Api/`, and the Core code that implements it.
  - Any exception type that Core raises for a failure the player can see.
  - Any App code that shows a failed Api result.

## Introduction

An Api call never throws. It returns an `OperationResult<T>`, and a failure carries an `ErrorInfo`: a catalog message id, its arguments, and an English detail for support. This standard covers the shape of an Api member and which calls take a cancellation token. It also covers how Core turns an exception into a result, how a new error gets its id, and how the App shows a failure.

### Purpose

- **Primary:** The App can show every failure in plain words without knowing how Core works. A failure reaches the screen as a catalog id that has copy and a look (D3), never as an exception the App must catch and interpret.
- **Secondary:** The fake backend and the real backend can tell the same story, because both speak results, not exceptions.
- **Side effect:** Api signatures say at a glance which calls a caller can cancel.

### Scope

C# in `Savepoint.Core` (the Api interfaces, `Services/`, `Errors/`) and in `Savepoint.App` where it reads a result.

## When to Apply

1. **Are you adding or changing a member of an Api interface?** Look at whether the file is in `src/Savepoint.Core/Api/`.
   - If yes, continue.
   - If no, go to question 3.
2. **Does the member only read memory, with no way to fail?** It returns state Core already holds, such as the job list, and touches no disk, process, or network.
   - If yes, see "Exception - in-memory reads" below.
   - If no, apply "Return `OperationResult<T>`" and "Match the token to the kind of call".
3. **Are you raising a new failure that the player can see?**
   - If yes, apply "Raise a `SavepointException` subclass with a literal message id".
   - If no, go to question 4.
4. **Are you writing App code that handles a failed result?**
   - If yes, apply "Show a failure through `ErrorPresentation`".
   - If no, this standard does not apply.

**Exception - in-memory reads:** A member that only reads state Core holds in memory, and cannot fail, returns the plain value. Its doc comment says so ("Reads memory and cannot fail"). `IJobsApi.List`, `IJobsApi.Get`, and `ILaunchApi.GetSession` are examples. Events on Api interfaces, such as `IJobsApi.JobChanged`, are also outside the result rule.

**Verification step:** list Api members that return a task without a result:

```bash
grep -nE "^\s+(Task|ValueTask)<" src/Savepoint.Core/Api/I*.cs | grep -v OperationResult
```

The search prints nothing.

## When NOT to Apply

- **Code inside Core that no Api member exposes.** An internal service can throw a `SavepointException` and let the Api layer convert it. It does not wrap its own returns in `OperationResult<T>`.
- **A bug in Core.** Do not invent a message id for a state that is a programming error. `ErrorInfo.From` turns an unexpected exception into `error.unknown`, which is the right answer for a bug. Add a specific id only for a failure a player can cause or fix.
- **App code that only passes a result along.** A view model that hands a result to `ErrorCardViewModel` does not inspect it, and needs nothing from this standard.

## Background

D3 replaced the plan's IPC layer with a service facade. The interfaces in `src/Savepoint.Core/Api/` are the contract between Core and App, and a fake backend implements the same interfaces for the App tests. D23 settled how cancellation works across that contract. A short mutation cannot be cancelled, because stopping one halfway leaves the catalog or the folders worse than finishing it.

## Coding Standard

### Return `OperationResult<T>` from every Api member that can fail

Every Api member that touches disk, git, a process, or the network returns `Task<OperationResult<T>>`. The remarks on `OperationResult<T>` state the rules for every member.

**Correct usage:**

```csharp
/// <summary>Lists every game in catalog order. Fails with <c>error.catalog.corrupt</c> when the games file is unreadable.</summary>
/// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
Task<OperationResult<IReadOnlyList<GameDefinition>>> ListAsync(CancellationToken cancellationToken = default);
```

**What to avoid:**

```csharp
// The App must now catch exceptions to show a failure.
Task<IReadOnlyList<GameDefinition>> ListAsync(CancellationToken cancellationToken = default);
```

**Examples:**

- `src/Savepoint.Core/Api/OperationResult.cs`, `OperationResult<T>` remarks: the rules every Api member follows.
- `src/Savepoint.Core/Api/IGamesApi.cs`, `IGamesApi.ListAsync`: a read that documents its failure id.

### Match the token to the kind of call

Every call that can be cancelled has a token. Where the token comes from depends on the kind of call:

| Kind of call | What it does | Cancellation |
| --- | --- | --- |
| Read | Returns state and changes nothing. | Takes `CancellationToken cancellationToken = default`. A cancelled read fails with `error.operation.cancelled`, which callers discard. |
| Job | Long work, such as a snapshot or a storage move. | Returns `OperationResult<JobStarted>` at once and takes no token. The App cancels it with `IJobsApi.CancelAsync`. The job's own token, `JobContext.CancellationToken`, flows all the way down through its work. |
| Short mutation | Changes the catalog, the settings, or a file, and finishes quickly. | Takes no token. |

A mutation that can take long enough that a player wants to stop it becomes a job.

**Correct usage:**

```csharp
Task<OperationResult<GameStorageUsage>> GetStorageUsageAsync(string gameId, CancellationToken cancellationToken = default);
Task<OperationResult<JobStarted>> OptimizeStorageAsync(string gameId, FreeUpSpaceToken? confirmed = null);
```

**What to avoid:**

```csharp
// A short mutation that a caller can stop halfway.
Task<OperationResult<string>> UseStorageFolderAsync(string folder, CancellationToken cancellationToken = default);
```

**Examples:**

- `src/Savepoint.Core/Api/IGamesApi.cs`, `IGamesApi.GetStorageUsageAsync` and `IGamesApi.OptimizeStorageAsync`: a read and a job side by side.
- `src/Savepoint.Core/Services/Jobs/JobContext.cs`, `JobContext.CancellationToken`: the token a job passes down.

**Known violations:**

- `src/Savepoint.Core/Api/IAppStatusApi.cs`, `IAppStatusApi.UseStorageFolderAsync`, `IAppStatusApi.RecoverCatalogFileAsync`, and `IAppStatusApi.StartWithEmptyStorageAsync`: mutations that take a token.
- `src/Savepoint.Core/Api/IGamesApi.cs`, `IGamesApi.ExportDefinitionAsync`: writes a file and takes a token.

### Convert exceptions with `ApiGuard.RunAsync`

An Api implementation runs its body through `ApiGuard.RunAsync`, with the backend's `IDiagnosticLog`. The guard maps a cancelled read to `error.operation.cancelled` and maps every other exception to a failure through `ErrorInfo.From`. It also writes the failure to the log (D34). Do not write a new try and catch that builds an `ErrorInfo` by hand. A hand-built one skips the log.

**Correct usage:**

```csharp
public Task<OperationResult<GameArtwork>> SetCustomImageAsync(string gameId, string imagePath) =>
    ApiGuard.RunAsync(_services.Log, async () =>
    {
```

**What to avoid:**

```csharp
// The failure never reaches the log, and the mapping can drift from ErrorInfo.From.
try { return OperationResult.Success(await DoWorkAsync()); }
catch (IOException ex) { return OperationResult.Failure<GameArtwork>(ex); }
```

**Examples:**

- `src/Savepoint.Core/Artwork/ArtworkApi.cs`, `ArtworkApi.SetCustomImageAsync`: a call through the guard.
- `src/Savepoint.Core/Services/ApiGuard.cs`, `ApiGuard.RunAsync`: the one place an exception becomes a result.
- `src/Savepoint.Core/Errors/ErrorInfo.cs`, `ErrorInfo.From`: a `SavepointException` keeps its id and arguments, and anything else becomes `error.unknown`.

### Raise a `SavepointException` subclass with a literal message id

A failure the player can see is a subclass of `SavepointException` in `src/Savepoint.Core/Errors/`. Its constructor passes a literal id of the form `error.<area>.<name>`, an English detail for support, and named arguments for the copy. The id is a literal, so the catalog lint tests can match it to the catalog. `CatalogLintTests.Source_never_builds_catalog_keys_at_run_time` fails on an id built at run time. Add the copy to `Localization/Assets/Lang/en/errors.json` and an entry to `ErrorPresentation.Table` in the same change.

**Correct usage:**

```csharp
public DiskSpaceException(string path, long requiredBytes, long freeBytes)
    : base(
        "error.diskSpace.low",
        $"Not enough free space at '{path}': {requiredBytes} bytes needed, {freeBytes} free.",
        new Dictionary<string, object?>
        {
            ["path"] = path,
            ["requiredBytes"] = requiredBytes,
            ["freeBytes"] = freeBytes,
        })
```

**What to avoid:**

```csharp
// The id is built at run time, so no test can match it to the catalog.
public StepFailedException(string step) : base("error.step." + step, $"The {step} step failed.")
```

**Examples:**

- `src/Savepoint.Core/Errors/DiskSpaceException.cs`, `DiskSpaceException`: a literal id with named arguments.
- `src/Savepoint.Core/Errors/SavepointException.cs`, `SavepointException`: the base type with `MessageId` and `Args`.
- `src/Savepoint.App/Features/Common/Errors/ErrorPresentation.cs`, `ErrorPresentation.Table`: the icon, tone, and title for each id.

### Show a failure through `ErrorPresentation`, never through `Detail`

The App looks up `ErrorInfo.MessageId` in `ErrorPresentation` and shows the catalog copy. `ErrorInfo.Detail` is English for support. It goes only into the text a player copies for a bug report, never into the screen and never into a branch.

**Correct usage:**

```csharp
var kind = ErrorPresentation.For(error.MessageId);
```

**What to avoid:**

```csharp
// Detail is English for support; its wording can change at any time.
if (error.Detail.Contains("disk full")) ShowDiskFullHint();
```

**Examples:**

- `src/Savepoint.App/Features/Common/Errors/ErrorCardViewModel.cs`, `ErrorCardViewModel`: looks up the presentation by id and keeps `Detail` for the copy-for-support text.

## Additional Resources

### Project Documentation

- `docs/architecture.md`, section "Errors": how an error travels from Core to the screen.
- `docs/decisions.md`, D3 (no IPC layer; a service facade), D23 (Api contract details, cancellation), D34 (the diagnostic log).
- [Injected side effects in Core](core-injected-side-effects.md): how Core reaches the clock, processes, and the network.
- [View models, views, and screen wiring](app-mvvm-view-models.md): how a view model calls the Api.

### External Resources

- [Task cancellation in .NET](https://learn.microsoft.com/dotnet/standard/parallel-programming/task-cancellation)
