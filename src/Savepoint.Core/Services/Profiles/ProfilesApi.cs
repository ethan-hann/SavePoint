using System.Collections.Concurrent;
using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Services.Profiles;

/// <summary>The real <see cref="IProfilesApi"/>: a profile is a pair of branches, one per folder, plus a catalog row.</summary>
internal sealed partial class ProfilesApi : IProfilesApi
{
    private const int _idAttempts = 8;

    private readonly BackendServices _services;
    private readonly CaptureRunner _capture;
    private readonly OptimizationService _optimization;

    // When a new profile saved only some of its folders, the retry for the rest joins the same capture event.
    private readonly ConcurrentDictionary<(string GameId, string ProfileId), PartialSave> _partial = new();

    public ProfilesApi(BackendServices services, OptimizationService optimization)
    {
        _services = services;
        _optimization = optimization;
        _capture = new CaptureRunner(services, optimization);
    }

    public Task<OperationResult<IReadOnlyList<Profile>>> ListAsync(string? gameId = null, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(() => _services.Catalog.ListProfilesAsync(gameId, cancellationToken), cancellationToken);

    public Task<OperationResult<JobStarted>> CreateAsync(string gameId, string name, string? notes, string pointName) =>
        ApiGuard.RunAsync(async () =>
        {
            var game = await RequireGameAsync(gameId).ConfigureAwait(false);
            var profile = CatalogValidator.RequireValid((await NewProfileAsync(gameId, name).ConfigureAwait(false)) with { Notes = notes });
            var captureId = NewCaptureId();
            var point = string.IsNullOrWhiteSpace(pointName) ? profile.Name : pointName;
            var roles = GameFolders.RolesOf(game);
            var jobId = _capture.Start(game, profile, point, roles, captureId, creating: true, result => RememberPartial(game, profile, result, captureId));
            return new JobStarted(jobId);
        });

    public Task<OperationResult<JobStarted>> CaptureAsync(string gameId, string profileId, string pointName, IReadOnlyCollection<RepoRole>? folders = null) =>
        ApiGuard.RunAsync(async () =>
        {
            var game = await RequireGameAsync(gameId).ConfigureAwait(false);
            var profile = await RequireProfileAsync(gameId, profileId).ConfigureAwait(false);
            var configured = GameFolders.RolesOf(game);
            IReadOnlyList<RepoRole> roles = folders is { Count: > 0 } ? [.. configured.Where(folders.Contains)] : configured;
            var point = string.IsNullOrWhiteSpace(pointName) ? profile.Name : pointName;
            var captureId = TakeCaptureId(gameId, profileId, roles);
            return new JobStarted(_capture.Start(game, profile, point, roles, captureId, creating: false));
        });

    public Task<OperationResult<Profile>> UpdateAsync(string gameId, string profileId, ProfileChanges changes) =>
        ApiGuard.NotBuilt<Profile>();

    public Task<OperationResult<Profile>> DuplicateAsync(string gameId, string sourceProfileId, string name) =>
        ApiGuard.NotBuilt<Profile>();

    public Task<OperationResult<IReadOnlyList<ProfileRowStats>>> GetRowStatsAsync(string gameId, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            async () =>
            {
                var game = await RequireGameAsync(gameId, cancellationToken).ConfigureAwait(false);
                var profiles = await _services.Catalog.ListProfilesAsync(gameId, cancellationToken).ConfigureAwait(false);
                return await _services.Storage.ReadAsync(
                    storage => ProfileStats.ReadAsync(_services, storage, game, profiles, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    public Task<OperationResult<ProfileDeletePreview>> GetDeletePreviewAsync(string gameId, string profileId, CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<ProfileDeletePreview>();

    public Task<OperationResult<Unit>> DeleteAsync(string gameId, string profileId) => ApiGuard.NotBuilt<Unit>();

    private static string NewCaptureId() => Guid.NewGuid().ToString("D");

    private async Task<GameDefinition> RequireGameAsync(string gameId, CancellationToken cancellationToken = default) =>
        await _services.Catalog.GetGameAsync(gameId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"There is no game '{gameId}'.");

    private async Task<Profile> RequireProfileAsync(string gameId, string profileId, CancellationToken cancellationToken = default) =>
        await _services.Catalog.GetProfileAsync(profileId, cancellationToken).ConfigureAwait(false) is { } profile && profile.GameId == gameId
            ? profile
            : throw new InvalidOperationException($"Game '{gameId}' has no profile '{profileId}'.");

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

    private void RememberPartial(GameDefinition game, Profile profile, CaptureResult result, string captureId)
    {
        var failed = result.Folders.Where(folder => !folder.Ok).Select(folder => folder.Role).ToList();
        if (failed.Count > 0 && failed.Count < result.Folders.Count)
        {
            _partial[(game.Id, profile.Id)] = new PartialSave(captureId, failed);
        }
    }

    // The retry of exactly the folders a partial save missed joins that save's capture event; anything else starts a new one.
    private string TakeCaptureId(string gameId, string profileId, IReadOnlyList<RepoRole> roles)
    {
        if (_partial.TryRemove((gameId, profileId), out var partial) && roles.All(partial.Missing.Contains))
        {
            return partial.CaptureId;
        }

        return NewCaptureId();
    }

    private sealed record PartialSave(string CaptureId, IReadOnlyList<RepoRole> Missing);
}
