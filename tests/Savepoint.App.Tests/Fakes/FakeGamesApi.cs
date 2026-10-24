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

    public IReadOnlyList<GameTemplate> Templates => GameTemplates.All;

    private FakeState State => _backend.State;

    public Task<OperationResult<IReadOnlyList<GameDefinition>>> ListAsync(CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(ListAsync), [], () => OperationResult.Success<IReadOnlyList<GameDefinition>>([.. State.Games]));

    public Task<OperationResult<AddGameOutcome>> AddAsync(GameDefinition definition) =>
        _backend.Call<AddGameOutcome>(_api, nameof(AddAsync), [definition], () =>
        {
            if (State.Orphans.TryGetValue(definition.Install.Path, out var orphan))
            {
                return OperationResult.Success<AddGameOutcome>(new AddGameOutcome.HistoryFound(orphan));
            }

            Add(definition);
            return OperationResult.Success<AddGameOutcome>(new AddGameOutcome.Added(definition));
        });

    public Task<OperationResult<GameDefinition>> ResolveOrphanAsync(GameDefinition definition, OrphanResolution resolution) =>
        _backend.Call(_api, nameof(ResolveOrphanAsync), [definition, resolution], () =>
        {
            if (!State.Orphans.Remove(definition.Install.Path))
            {
                throw new KeyNotFoundException($"No old history is scripted at '{definition.Install.Path}'.");
            }

            if (resolution is OrphanResolution.Reconnect reconnect)
            {
                var game = definition with { Id = reconnect.ExistingGameId };
                Add(game);
                foreach (var recovered in reconnect.Profiles)
                {
                    var id = recovered.BranchName.StartsWith(_branchPrefix, StringComparison.Ordinal)
                        ? recovered.BranchName[_branchPrefix.Length..]
                        : recovered.BranchName;
                    State.Profiles.Add(new Profile(id, game.Id, recovered.Name, recovered.BranchName));
                }

                return OperationResult.Success(game);
            }

            Add(definition);
            return OperationResult.Success(definition);
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
            var foldersChanged = previous.Install != definition.Install || previous.External != definition.External;
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

    public Task<OperationResult<IReadOnlyList<FolderStatus>>> GetFolderStatusAsync(string gameId, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(GetFolderStatusAsync), [gameId], () =>
        {
            var game = State.Game(gameId);
            return OperationResult.Success(State.FolderStatuses.GetValueOrDefault(gameId) ?? Statuses(game, new ConsentStatus(true, null)));
        });

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
            var empty = new CapturePreview(gameId, [.. FakeState.FoldersOf(game).Select(r => new FolderCapturePreview(r, State.Resolve(PathOf(game, r)), 0, 0, 0))], 0);
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

    public Task<OperationResult<string>> ResolvePathAsync(string rawPath, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(ResolvePathAsync), [rawPath], () => OperationResult.Success(State.Resolve(rawPath)));

    public Task<OperationResult<IReadOnlyList<string>>> ListExecutablesAsync(string installPath, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(ListExecutablesAsync), [installPath], () =>
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

    public Task<OperationResult<Unit>> ExportDefinitionAsync(string gameId, string filePath, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(ExportDefinitionAsync), [gameId, filePath], () =>
        {
            State.Game(gameId);
            return OperationResult.Success();
        });

    internal static string PathOf(GameDefinition game, RepoRole role) =>
        role == RepoRole.Install ? game.Install.Path : game.External?.Path ?? "";

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
