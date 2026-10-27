using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Launch;

namespace Savepoint.App.Tests.Fakes;

public sealed class FakeGamesApi : IGamesApi
{
    private const string _api = nameof(IGamesApi);
    private const string _branchPrefix = "profile/";

    private readonly FakeBackend _backend;

    internal FakeGamesApi(FakeBackend backend)
    {
        _backend = backend;
    }

    // A test sets this to offer its own templates in place of the shipped ones.
    public IReadOnlyList<GameTemplate>? TemplatesOverride { get; set; }

    public IReadOnlyList<GameTemplate> Templates => TemplatesOverride ?? GameTemplates.All;

    private FakeState State => _backend.State;

    // A test sets this to hold the next listing, as a slow drive would.
    public Func<Task>? ListGate { get; set; }

    public async Task<OperationResult<IReadOnlyList<GameDefinition>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (ListGate is { } gate)
        {
            ListGate = null;
            await gate();
        }

        return await _backend.Call(_api, nameof(ListAsync), [], () => OperationResult.Success<IReadOnlyList<GameDefinition>>([.. State.Games]));
    }

    // The rules the editor said SavePoint supplied, for the last add or reconnect, so a test can see what the form sent.
    public IReadOnlyList<string>? LastSuppliedRules { get; private set; }

    public Task<OperationResult<AddGameOutcome>> AddAsync(GameDefinition definition, IReadOnlyList<string>? suppliedRules = null) =>
        _backend.Call<AddGameOutcome>(_api, nameof(AddAsync), [definition], () =>
        {
            LastSuppliedRules = suppliedRules;
            if (State.Orphans.TryGetValue(definition.Install.Path, out var orphan))
            {
                return OperationResult.Success<AddGameOutcome>(new AddGameOutcome.HistoryFound(orphan));
            }

            var (kept, leftOff) = WithoutRulesThatHideSaves(definition, suppliedRules);
            Add(kept);
            return OperationResult.Success<AddGameOutcome>(new AddGameOutcome.Added(kept, leftOff));
        });

    // Like the service, a default rule that would leave out files of the saves folder moves to the game's own folder only; when the check cannot run every one does.
    private (GameDefinition Game, RulesLeftOff? LeftOff) WithoutRulesThatHideSaves(GameDefinition definition, IReadOnlyList<string>? suppliedRules)
    {
        var asked = definition.IgnorePaths.Where(rule => RunTimeOutput.DefaultRules.Contains(rule) && suppliedRules?.Contains(rule) == true).ToList();
        var hiding = SavesCheckFails ? asked : asked.Where(SavesLeftOutByRule.ContainsKey).ToList();
        return hiding.Count == 0
            ? (definition, null)
            : (definition with
            {
                IgnorePaths = [.. definition.IgnorePaths.Where(rule => !hiding.Contains(rule))],
                InstallIgnorePaths = RunTimeOutput.Merge(definition.InstallIgnorePaths, hiding),
            }, new RulesLeftOff(hiding, SavesHoldFiles: !SavesCheckFails));
    }

    // The files of the saves folder each rule would leave out, as a test sets them; a rule not listed matches nothing.
    public Dictionary<string, IReadOnlyList<string>> SavesLeftOutByRule { get; } = [];

    // A test sets this to make the saves check fail, as an unreadable drive would.
    public bool SavesCheckFails { get; set; }

    // A test sets this to hold a scan, as a slow drive would; it may ignore the token.
    public Func<CancellationToken, Task>? ScanGate { get; set; }

    // The token each scan was given, in call order.
    public List<CancellationToken> ScanTokens { get; } = [];

    public async Task<OperationResult<LibraryScan>> ScanLibrariesAsync(CancellationToken cancellationToken = default)
    {
        ScanTokens.Add(cancellationToken);
        if (ScanGate is { } gate)
        {
            await gate(cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Failure<LibraryScan>(new RequestCancelledException());
        }

        return await _backend.Call(_api, nameof(ScanLibrariesAsync), [], () =>
        {
            var scripted = State.Library ?? new LibraryScan([], false, false);
            var games = scripted.Games.Select(game => game with { AddedGameId = State.Games.Find(g => SameFolder(g.Install.Path, game.InstallPath))?.Id ?? game.AddedGameId });
            return OperationResult.Success(scripted with { Games = [.. games] });
        });
    }

    public Task<OperationResult<LibraryAddResult>> AddLibraryGameAsync(LibraryGame game) =>
        _backend.Call(_api, nameof(AddLibraryGameAsync), [game], () =>
        {
            if (State.Games.Find(g => SameFolder(g.Install.Path, game.InstallPath)) is { } there)
            {
                return OperationResult.Success(new LibraryAddResult(game.Key, game.Name, LibraryAddStatus.AlreadyAdded, there.Id));
            }

            if (State.LibraryRefusals.TryGetValue(game.Key, out var refusal))
            {
                return OperationResult.Success(new LibraryAddResult(game.Key, game.Name, LibraryAddStatus.Refused, Error: refusal));
            }

            if (State.Orphans.ContainsKey(game.Draft.Install.Path))
            {
                return OperationResult.Success(new LibraryAddResult(game.Key, game.Name, LibraryAddStatus.HasOlderHistory));
            }

            // Like the service, a folder the template names that is not there yet is left out of the game.
            var offered = game.Draft;
            offered = game.SavesFolder is not null && !game.SavesFolderExists ? offered with { External = null } : offered;
            offered = game.ModsFolder is not null && !game.ModsFolderExists ? offered with { Mods = null } : offered;
            var (draft, leftOff) = WithoutRulesThatHideSaves(offered, game.Draft.IgnorePaths);
            Add(draft);
            return OperationResult.Success(new LibraryAddResult(
                game.Key,
                game.Name,
                LibraryAddStatus.Added,
                draft.Id,
                SavesFolderLeftOut: draft.External is null ? game.SavesFolder : null,
                RulesLeftOff: leftOff,
                ModsFolderLeftOut: draft.Mods is null ? game.ModsFolder : null));
        });

    private static bool SameFolder(string a, string b) =>
        string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    public Task<OperationResult<AddGameOutcome.Added>> ResolveOrphanAsync(GameDefinition definition, OrphanResolution resolution, IReadOnlyList<string>? suppliedRules = null) =>
        _backend.Call(_api, nameof(ResolveOrphanAsync), [definition, resolution], () =>
        {
            LastSuppliedRules = suppliedRules;
            if (!State.Orphans.Remove(definition.Install.Path))
            {
                throw new KeyNotFoundException($"No old history is scripted at '{definition.Install.Path}'.");
            }

            if (resolution is OrphanResolution.Reconnect reconnect)
            {
                var (game, reconnectedLeftOff) = WithoutRulesThatHideSaves(definition with { Id = reconnect.ExistingGameId }, suppliedRules);
                Add(game);
                foreach (var recovered in reconnect.Profiles)
                {
                    var id = recovered.BranchName.StartsWith(_branchPrefix, StringComparison.Ordinal)
                        ? recovered.BranchName[_branchPrefix.Length..]
                        : recovered.BranchName;
                    State.Profiles.Add(new Profile(id, game.Id, recovered.Name, recovered.BranchName));
                }

                return OperationResult.Success(new AddGameOutcome.Added(game, reconnectedLeftOff));
            }

            var (fresh, freshLeftOff) = WithoutRulesThatHideSaves(definition, suppliedRules);
            Add(fresh);
            return OperationResult.Success(new AddGameOutcome.Added(fresh, freshLeftOff));
        });

    public Task<OperationResult<GameDefinition>> UpdateAsync(GameDefinition definition, bool confirmFolderChange = false) =>
        _backend.Call(_api, nameof(UpdateAsync), [definition, confirmFolderChange], () =>
        {
            var index = State.Games.FindIndex(g => g.Id == definition.Id);
            if (index < 0)
            {
                throw new GameNotFoundException(definition.Id);
            }

            var previous = State.Games[index];
            var foldersChanged = previous.Install != definition.Install || previous.External != definition.External || previous.Mods != definition.Mods;
            if (foldersChanged && State.IsOpen(definition.Id))
            {
                return OperationResult.Failure<GameDefinition>(new GameRunningException(previous.Id, previous.Name));
            }

            State.Games[index] = definition;
            if (foldersChanged)
            {
                State.FolderStatuses[definition.Id] = ConfirmedNow(definition);
            }

            return OperationResult.Success(definition);
        });

    public Task<OperationResult<Unit>> DeleteAsync(string gameId) =>
        _backend.Call(_api, nameof(DeleteAsync), [gameId], () =>
        {
            var game = State.Game(gameId);
            if (State.IsOpen(gameId))
            {
                return OperationResult.Failure<Unit>(new GameRunningException(game.Id, game.Name));
            }

            State.RemoveGame(gameId);
            return OperationResult.Success();
        });

    // A test sets this to hold the answer of a folder status read, as a slow drive would.
    public Func<Task>? FolderStatusGate { get; set; }

    public async Task<OperationResult<IReadOnlyList<FolderStatus>>> GetFolderStatusAsync(string gameId, CancellationToken cancellationToken = default)
    {
        var answer = await _backend.Call(_api, nameof(GetFolderStatusAsync), [gameId], () =>
        {
            var game = State.Game(gameId);
            return OperationResult.Success(State.FolderStatuses.GetValueOrDefault(gameId) ?? Statuses(game, new ConsentStatus(true, null)));
        });
        if (FolderStatusGate is { } gate)
        {
            await gate();
        }

        return answer;
    }

    // What a test says each game's store reports; a game not listed is unknown, as the real Api answers for a store it cannot read.
    public Dictionary<string, CloudSaveInfo> CloudSaves { get; } = [];

    // A test sets this to hold the answer, as a slow disk would.
    public Func<Task>? CloudSavesGate { get; set; }

    // A test sets this to answer a check made from the form's values; it may hold the answer, as a slow disk would.
    public Func<CloudSaveQuery, CancellationToken, Task<CloudSaveInfo>>? CloudSavesForForm { get; set; }

    // Every check made from the form, in call order, so a test can wait for one to finish.
    public List<Task<OperationResult<CloudSaveInfo>>> FormChecks { get; } = [];

    public Task<OperationResult<CloudSaveInfo>> GetCloudSavesAsync(CloudSaveQuery query, CancellationToken cancellationToken = default)
    {
        var check = CheckFormAsync(query, cancellationToken);
        FormChecks.Add(check);
        return check;
    }

    private async Task<OperationResult<CloudSaveInfo>> CheckFormAsync(CloudSaveQuery query, CancellationToken cancellationToken)
    {
        // Like the real Api, a token that is already cancelled fails the call before anything is read.
        if (cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Failure<CloudSaveInfo>(new RequestCancelledException());
        }

        var answer = await _backend.Call(_api, nameof(GetCloudSavesAsync), [query], () => OperationResult.Success(new CloudSaveInfo(CloudSaveState.Unknown, CloudStore.None)));
        if (!answer.Ok || CloudSavesForForm is not { } scripted)
        {
            return answer;
        }

        try
        {
            return OperationResult.Success(await scripted(query, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Failure<CloudSaveInfo>(new RequestCancelledException());
        }
    }

    public async Task<OperationResult<CloudSaveInfo>> GetCloudSavesAsync(string gameId, CancellationToken cancellationToken = default)
    {
        var answer = await _backend.Call(_api, nameof(GetCloudSavesAsync), [gameId], () =>
        {
            State.Game(gameId);
            return OperationResult.Success(CloudSaves.GetValueOrDefault(gameId) ?? new CloudSaveInfo(CloudSaveState.Unknown, CloudStore.None));
        });
        if (CloudSavesGate is { } gate)
        {
            await gate();
        }

        return answer;
    }

    public Task<OperationResult<GameStorageUsage>> GetStorageUsageAsync(string gameId, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(GetStorageUsageAsync), [gameId], () =>
        {
            State.Game(gameId);
            return OperationResult.Success(new GameStorageUsage(gameId, State.GameBytes.GetValueOrDefault(gameId), State.VolumeTotalBytes, State.VolumeFreeBytes));
        });

    public Task<OperationResult<CapturePreview>> GetCapturePreviewAsync(string gameId, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(GetCapturePreviewAsync), [gameId], () =>
        {
            var game = State.Game(gameId);
            // As the real backend answers: a folder with no profile to hold it is blocked by the game having no profile yet.
            var blocked = State.Profiles.Any(profile => profile.GameId == gameId) ? StartEmptyBlock.NewChanges : StartEmptyBlock.FirstProfile;
            var empty = new CapturePreview(
                gameId,
                [.. FakeState.FoldersOf(game).Select(r => new FolderCapturePreview(r, State.Resolve(PathOf(game, r)), 0, 0, 0, State.FolderHeld(gameId, r), r == RepoRole.Install || State.FolderHeld(gameId, r) ? StartEmptyBlock.None : blocked))],
                0);
            return OperationResult.Success(State.CapturePreviews.GetValueOrDefault(gameId) ?? empty);
        });

    public Task<OperationResult<JobStarted>> OptimizeStorageAsync(string gameId, FreeUpSpaceToken? confirmed = null) =>
        _backend.Call(_api, nameof(OptimizeStorageAsync), [gameId, confirmed], () =>
        {
            var game = State.Game(gameId);
            var done = new OptimizeResult([FakeSettingsApi.Optimized(game)]);
            // Once the job has run, the game's recently removed snapshots the player agreed to lose are gone for good and no others, as in the real backend.
            return OperationResult.Success(new JobStarted(_backend.Jobs.Start(
                JobKind.Compact,
                gameId,
                done,
                job =>
                {
                    if (job.Status is JobStatus.Succeeded or JobStatus.Partial)
                    {
                        State.DeleteRemoved(confirmed, gameId);
                    }

                    _backend.Settings.MarkOptimized(job);
                },
                [JobLine.ForStep(JobSteps.CompactGame, ("game", game.Name))])));
        });

    public Task<OperationResult<FreeUpSpacePreview>> GetFreeUpSpacePreviewAsync(string gameId, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(GetFreeUpSpacePreviewAsync), [gameId], () =>
        {
            State.Game(gameId);
            return OperationResult.Success(State.FreeUpPreview(gameId));
        });

    // A test sets this to keep every estimate waiting until it completes it, as a big store would; a cancelled wait fails as cancelled.
    public TaskCompletionSource? EstimateHold { get; set; }

    public async Task<OperationResult<long>> EstimateFreeUpSpaceAsync(string gameId, FreeUpSpaceToken token, CancellationToken cancellationToken = default)
    {
        try
        {
            if (EstimateHold is { } hold)
            {
                await hold.Task.WaitAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException cancelled)
        {
            return OperationResult.Failure<long>(cancelled);
        }

        return await _backend.Call(_api, nameof(EstimateFreeUpSpaceAsync), [gameId, token], () =>
        {
            State.Game(gameId);
            return OperationResult.Success(State.FreeUpEstimate);
        });
    }

    public Task<OperationResult<string>> ResolvePathAsync(string rawPath, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(ResolvePathAsync), [rawPath], () => OperationResult.Success(State.Resolve(rawPath)));

    public Task<OperationResult<bool>> FolderExistsAsync(string rawPath, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(FolderExistsAsync), [rawPath], () =>
            OperationResult.Success(!State.MissingFolders.Contains(rawPath) && !State.MissingFolders.Contains(State.Resolve(rawPath))));

    public Task<OperationResult<IReadOnlyList<string>>> ListExecutablesAsync(string installPath, string gameName, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(ListExecutablesAsync), [installPath, gameName], () =>
        {
            if (State.Executables.TryGetValue(installPath, out var scripted))
            {
                return OperationResult.Success(scripted);
            }

            var game = State.Games.Find(g => string.Equals(g.Install.Path, installPath, StringComparison.OrdinalIgnoreCase));
            return OperationResult.Success<IReadOnlyList<string>>(game?.Launch.ExePath is { } exe ? [exe] : []);
        });

    // Every scripted program counts as the same size, so only names and folders decide the answer.
    public async Task<OperationResult<string>> SuggestProcessNameAsync(string installPath, string gameName, CancellationToken cancellationToken = default)
    {
        // The answer is worked out when the call is made; the gate only delays when the caller sees it.
        var answer = await _backend.Call(_api, nameof(SuggestProcessNameAsync), [installPath, gameName], () =>
        {
            var programs = State.Executables.TryGetValue(installPath, out var scripted) ? scripted : [];
            return OperationResult.Success(
                ProcessNameSuggester.Suggest(null, gameName, programs.Select(path => new InstalledProgram(path, 5 << 20))) ?? string.Empty);
        });
        SuggestTokens.Add(cancellationToken);
        if (SuggestGate is { } gate)
        {
            await gate(installPath, cancellationToken);
        }

        return answer;
    }

    // A test sets this to hold the answer of a suggestion, so a late first answer can arrive after a newer one.
    public Func<string, CancellationToken, Task>? SuggestGate { get; set; }

    // The token each suggestion call was given, in call order.
    public List<CancellationToken> SuggestTokens { get; } = [];

    public Task<OperationResult<GameDefinition>> ImportDefinitionAsync(string filePath, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(ImportDefinitionAsync), [filePath], () =>
            State.Imports.TryGetValue(filePath, out var definition)
                ? OperationResult.Success(definition)
                : throw new KeyNotFoundException($"No import is scripted for '{filePath}'."));

    public Task<OperationResult<Unit>> ExportDefinitionAsync(string gameId, string filePath) =>
        _backend.Call(_api, nameof(ExportDefinitionAsync), [gameId, filePath], () =>
        {
            State.Game(gameId);
            return OperationResult.Success();
        });

    internal static string PathOf(GameDefinition game, RepoRole role) => role switch
    {
        RepoRole.Install => game.Install.Path,
        RepoRole.Mods => game.Mods?.Path ?? "",
        _ => game.External?.Path ?? "",
    };

    private void Add(GameDefinition game)
    {
        if (State.Games.Exists(g => g.Id == game.Id))
        {
            throw new InvalidOperationException($"The fake already has a game '{game.Id}'.");
        }

        State.Games.Add(game);
        State.GameBytes[game.Id] = 0;
        State.FolderStatuses[game.Id] = ConfirmedNow(game);
    }

    // Saving a game is where the user confirms its folders, so a new or moved folder is confirmed as of now.
    private IReadOnlyList<FolderStatus> ConfirmedNow(GameDefinition game) => Statuses(game, new ConsentStatus(true, _backend.Clock.Now));

    private IReadOnlyList<FolderStatus> Statuses(GameDefinition game, ConsentStatus consent) =>
        [.. FakeState.FoldersOf(game).Select(r => new FolderStatus(r, PathOf(game, r), State.Resolve(PathOf(game, r)), true, consent))];
}
