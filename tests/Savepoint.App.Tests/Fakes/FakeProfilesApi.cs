using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Fakes;

public sealed class FakeProfilesApi : IProfilesApi
{
    private const string _api = nameof(IProfilesApi);

    private readonly FakeBackend _backend;

    internal FakeProfilesApi(FakeBackend backend)
    {
        _backend = backend;
    }

    private FakeState State => _backend.State;

    public Task<OperationResult<IReadOnlyList<Profile>>> ListAsync(string? gameId = null, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(ListAsync), [gameId], () =>
            OperationResult.Success<IReadOnlyList<Profile>>([.. State.Profiles.Where(p => gameId is null || p.GameId == gameId)]));

    // Running, last played, and the override are read live, so launches and edits show without re-seeding stats.
    public Task<OperationResult<IReadOnlyList<ProfileRowStats>>> GetRowStatsAsync(string gameId, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(GetRowStatsAsync), [gameId], () =>
        {
            State.Game(gameId);
            var running = State.IsOpen(gameId) ? State.Sessions[gameId].ProfileId : null;
            return OperationResult.Success<IReadOnlyList<ProfileRowStats>>(
                [
                    .. State.Profiles.Where(p => p.GameId == gameId).Select(p => State.StatsFor(p) with
                    {
                        IsRunning = p.Id == running,
                        LastPlayedAt = p.LastLaunchedAt ?? State.StatsFor(p).LastPlayedAt,
                        VersionOverride = p.VersionOverride,
                    }),
                ]);
        });

    // The profile is listed at once and its capture job is left for the test to end.
    public Task<OperationResult<JobStarted>> CreateAsync(string gameId, string name, string? notes, string pointName) =>
        _backend.Call(_api, nameof(CreateAsync), [gameId, name, notes, pointName], () =>
        {
            var game = State.Game(gameId);
            var profile = CatalogIds.NewProfile(gameId, name, State.Random) with { Notes = notes };
            State.Profiles.Add(profile);
            return OperationResult.Success(StartCapture(game, profile, FakeState.FoldersOf(game)));
        });

    public Task<OperationResult<Profile>> UpdateAsync(string gameId, string profileId, ProfileChanges changes) =>
        _backend.Call(_api, nameof(UpdateAsync), [gameId, profileId, changes], () =>
        {
            var profile = State.Profile(gameId, profileId);
            var updated = profile with { Name = changes.Name, Notes = changes.Notes, VersionOverride = changes.VersionOverride };
            State.Profiles[State.Profiles.IndexOf(profile)] = updated;
            return OperationResult.Success(updated);
        });

    public Task<OperationResult<Profile>> DuplicateAsync(string gameId, string sourceProfileId, string name) =>
        _backend.Call(_api, nameof(DuplicateAsync), [gameId, sourceProfileId, name], () =>
        {
            var source = State.Profile(gameId, sourceProfileId);
            var copy = CatalogIds.NewProfile(gameId, name, State.Random) with { Notes = source.Notes, VersionOverride = source.VersionOverride };
            State.Profiles.Add(copy);
            State.RowStats[copy.Id] = State.StatsFor(source) with
            {
                ProfileId = copy.Id,
                IsActive = false,
                UnsavedChanges = [],
                LastPlayedAt = null,
                IsRunning = false,
            };
            return OperationResult.Success(copy);
        });

    // Unless scripted, the points lost are the ones this profile captured, counted per folder they touched (D17).
    public Task<OperationResult<ProfileDeletePreview>> GetDeletePreviewAsync(string gameId, string profileId, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(GetDeletePreviewAsync), [gameId, profileId], () =>
        {
            var game = State.Game(gameId);
            var profile = State.Profile(gameId, profileId);
            if (State.DeletePreviews.TryGetValue(profileId, out var scripted))
            {
                return OperationResult.Success(scripted);
            }

            var points = State.PointsOf(gameId).Where(p => p.ProfileId == profileId).ToList();
            var folders = new List<FolderPointLoss> { new(RepoRole.Install, points.Count(p => p.Kind != HistoryPointKind.SaveOnly), null) };
            if (game.External is not null)
            {
                folders.Add(new FolderPointLoss(RepoRole.External, points.Count(p => p.Kind != HistoryPointKind.VersionAndMods), null));
            }

            return OperationResult.Success(new ProfileDeletePreview(profileId, points.Count, folders, DeleteBlocker(game, profile)));
        });

    public Task<OperationResult<Unit>> DeleteAsync(string gameId, string profileId) =>
        _backend.Call(_api, nameof(DeleteAsync), [gameId, profileId], () =>
        {
            var game = State.Game(gameId);
            var profile = State.Profile(gameId, profileId);
            if (DeleteBlocker(game, profile) is { } blocker)
            {
                return OperationResult.Failure<Unit>(blocker);
            }

            State.RemoveProfile(profile);
            return OperationResult.Success();
        });

    public Task<OperationResult<JobStarted>> CaptureAsync(string gameId, string profileId, string pointName, IReadOnlyCollection<RepoRole>? folders = null) =>
        _backend.Call(_api, nameof(CaptureAsync), [gameId, profileId, pointName, folders], () =>
        {
            var game = State.Game(gameId);
            var profile = State.Profile(gameId, profileId);
            return OperationResult.Success(StartCapture(game, profile, folders ?? FakeState.FoldersOf(game)));
        });

    private JobStarted StartCapture(GameDefinition game, Profile profile, IEnumerable<RepoRole> folders)
    {
        var saved = new CaptureResult(profile.Id, State.NewPointId(), [.. folders.Select(r => new FolderOutcome(r))]);
        return new JobStarted(_backend.Jobs.Start(JobKind.Capture, game.Id, saved));
    }

    // A running game blocks every destructive step (D13.5); the active profile cannot go either.
    private ErrorInfo? DeleteBlocker(GameDefinition game, Profile profile)
    {
        if (State.IsOpen(game.Id))
        {
            return ErrorInfo.From(new GameRunningException(game.Id, game.Name));
        }

        return State.StatsFor(profile).IsActive ? ErrorInfo.From(new ProfileActiveException(profile.Id)) : null;
    }
}
