using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Services.History;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Services.Profiles;

/// <summary>The real <see cref="IProfilesApi"/>: a profile is a pair of branches, one per folder, plus a catalog row.</summary>
internal sealed partial class ProfilesApi : IProfilesApi
{
    private const int _idAttempts = 8;

    private readonly BackendServices _services;
    private readonly CaptureRunner _capture;
    private readonly OptimizationService _optimization;
    private readonly RemovedSnapshots _removed;

    public ProfilesApi(BackendServices services, OptimizationService optimization)
    {
        _services = services;
        _optimization = optimization;
        _capture = services.Capture;
        _removed = new RemovedSnapshots(new HistoryReader(services));
    }

    public Task<OperationResult<IReadOnlyList<Profile>>> ListAsync(string? gameId = null, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(_services.Log, () => _services.Catalog.ListProfilesAsync(gameId, cancellationToken), cancellationToken);

    public Task<OperationResult<JobStarted>> CreateAsync(string gameId, string name, string? notes, string pointName) =>
        ApiGuard.RunAsync(_services.Log, async () =>
        {
            var game = await RequireGameAsync(gameId).ConfigureAwait(false);
            var profile = CatalogValidator.RequireValid((await NewProfileAsync(gameId, name).ConfigureAwait(false)) with { Notes = notes });
            var capture = _capture.NewEvent();
            var point = string.IsNullOrWhiteSpace(pointName) ? profile.Name : pointName;
            var roles = GameFolders.RolesOf(game);
            var jobId = _capture.Start(game, profile, point, roles, capture, creating: true);
            return new JobStarted(jobId);
        });

    public Task<OperationResult<JobStarted>> CaptureAsync(string gameId, string profileId, string pointName, IReadOnlyCollection<RepoRole>? folders = null) =>
        ApiGuard.RunAsync(_services.Log, async () =>
        {
            var game = await RequireGameAsync(gameId).ConfigureAwait(false);
            var profile = await RequireProfileAsync(gameId, profileId).ConfigureAwait(false);
            var configured = GameFolders.RolesOf(game);
            IReadOnlyList<RepoRole> roles = folders is { Count: > 0 } ? [.. configured.Where(folders.Contains)] : configured;
            var point = string.IsNullOrWhiteSpace(pointName) ? profile.Name : pointName;
            var (capture, forksNewBranches) = _capture.TakeEvent(gameId, profileId, roles);
            return new JobStarted(_capture.Start(game, profile, point, roles, capture, creating: false, forksNewBranches: forksNewBranches));
        });

    public Task<OperationResult<IReadOnlyList<ProfileRowStats>>> GetRowStatsAsync(string gameId, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                var game = await RequireGameAsync(gameId, cancellationToken).ConfigureAwait(false);
                var profiles = await _services.Catalog.ListProfilesAsync(gameId, cancellationToken).ConfigureAwait(false);
                return await _services.Storage.ReadAsync(
                    storage => ProfileStats.ReadAsync(_services, storage, game, profiles, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);


    private async Task<GameDefinition> RequireGameAsync(string gameId, CancellationToken cancellationToken = default) =>
        await _services.Catalog.GetGameAsync(gameId, cancellationToken).ConfigureAwait(false)
        ?? throw new GameNotFoundException(gameId);

    private async Task<Profile> RequireProfileAsync(string gameId, string profileId, CancellationToken cancellationToken = default) =>
        await _services.Catalog.GetProfileAsync(profileId, cancellationToken).ConfigureAwait(false) is { } profile && profile.GameId == gameId
            ? profile
            : throw new ProfileNotFoundException(gameId, profileId);

    // A fresh id that no profile of any game has; the branch is named after it.
    private async Task<Profile> NewProfileAsync(string gameId, string name)
    {
        for (var attempt = 0; attempt < _idAttempts; attempt++)
        {
            var profile = CatalogIds.NewProfile(gameId, name);
            if (await _services.Catalog.GetProfileAsync(profile.Id).ConfigureAwait(false) is null)
            {
                return profile;
            }
        }

        throw new InvalidOperationException("No unused profile id was found.");
    }
}
