using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Services.History;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Services.Games;

internal sealed partial class GamesApi
{
    public Task<OperationResult<Unit>> DeleteAsync(string gameId) =>
        ApiGuard.RunAsync(_services.Log, () => DeleteCoreAsync(gameId));

    public Task<OperationResult<JobStarted>> OptimizeStorageAsync(string gameId, FreeUpSpaceToken? confirmed = null) =>
        ApiGuard.RunAsync(_services.Log, async () =>
        {
            var game = await RequireGameAsync(gameId, CancellationToken.None).ConfigureAwait(false);

            // The user asked for this one, so a launch or capture waits for it instead of stopping it.
            return new JobStarted(_optimization.Start(game.Id, OptimizationMode.Reclaim, preemptible: false, confirmed, byRequest: true));
        });

    public Task<OperationResult<FreeUpSpacePreview>> GetFreeUpSpacePreviewAsync(string gameId, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                var game = await RequireGameAsync(gameId, cancellationToken).ConfigureAwait(false);
                var profiles = await _services.Catalog.ListProfilesAsync(game.Id, cancellationToken).ConfigureAwait(false);
                return await _services.Storage.ReadAsync(
                    storage => RemovedSnapshots.PreviewAsync(storage, game.Id, GameFolders.ReposForGitDirOnly(_services, storage, game), profiles, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    private async Task<Unit> DeleteCoreAsync(string gameId)
    {
        return await _services.RunWithLocksAsync(
            [BackendServices.LibraryLockKey, gameId],
            () => _services.Storage.ReadAsync(async storage =>
            {
                var game = await RequireGameAsync(gameId, CancellationToken.None).ConfigureAwait(false);
                if (_services.Sessions.IsGameRunning(game.Id))
                {
                    throw SwitchInProgressException.Refusal(_services.Sessions, game.Id, game.Name);
                }

                // The engine takes the markers out of the game's folders with the repositories; a folder that cannot be found is skipped.
                var bytes = await DirectorySize.GetBytesAsync(storage.GameDirectory(game.Id), CancellationToken.None).ConfigureAwait(false);
                var removed = new List<string>();
                foreach (var folder in GameFolders.ResolveLoosely(_services.PathTokens, game).Where(folder => folder.Path is not null))
                {
                    await storage.Engine.DeleteRepoAsync(GameFolders.Repo(storage, game.Id, folder)).ConfigureAwait(false);
                    removed.Add(GameFolders.LogName(folder.Role));
                }

                await StorageFolders.DeleteAsync(storage, storage.GameDirectory(game.Id), CancellationToken.None).ConfigureAwait(false);
                var profiles = await _services.Catalog.ListProfilesAsync(game.Id).ConfigureAwait(false);
                foreach (var profile in profiles)
                {
                    await _services.Catalog.DeleteProfileAsync(profile.Id).ConfigureAwait(false);
                }

                _services.Log.Information($"Game deleted: {game.Name} ({game.Id}); the history of its {string.Join(" and ", removed)} folders was removed ({bytes} bytes of storage), with {profiles.Count} profiles. The game's own folders were not touched.");

                await _services.Catalog.DeleteGameAsync(game.Id).ConfigureAwait(false);

                // After the catalog forgets the game, so a lookup still running for it finds nothing to write to.
                _services.Artwork.DeleteGame(game.Id);
                return Unit.Value;
            }));
    }
}
