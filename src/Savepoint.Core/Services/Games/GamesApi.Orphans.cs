using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Services.Games;

internal sealed partial class GamesApi
{
    public Task<OperationResult<GameDefinition>> ResolveOrphanAsync(GameDefinition definition, OrphanResolution resolution) =>
        ApiGuard.RunAsync(() => ResolveOrphanCoreAsync(definition, resolution));

    private async Task<GameDefinition> ResolveOrphanCoreAsync(GameDefinition definition, OrphanResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var reconnect = resolution as OrphanResolution.Reconnect;

        // A reconnected game takes back the id its history was saved under.
        var game = CatalogValidator.RequireValid(reconnect is null ? definition : definition with { Id = reconnect.ExistingGameId });
        return await _services.RunWithLocksAsync(
            [BackendServices.LibraryLockKey, game.Id],
            () => _services.Storage.ReadAsync(async storage =>
            {
                var games = await _services.Catalog.ListGamesAsync().ConfigureAwait(false);
                if (games.Any(existing => existing.Id == game.Id))
                {
                    throw new GameAlreadyExistsException(game.Id);
                }

                var folders = GameFolders.ResolveStrictly(_services.PathTokens, game);
                Vet(storage, game, folders, games);
                return reconnect is null
                    ? await StartFreshAsync(storage, game, folders, games).ConfigureAwait(false)
                    : await ReconnectAsync(storage, game, folders, reconnect).ConfigureAwait(false);
            }));
    }

    // The old history stays on disk but nothing reaches it any more; only the claim on the folders is let go.
    private async Task<GameDefinition> StartFreshAsync(StorageSnapshot storage, GameDefinition game, IReadOnlyList<GameFolder> folders, IReadOnlyList<GameDefinition> games)
    {
        var orphans = await OrphanScanner.FindAsync(storage, game.Id, folders, [.. games.Select(g => g.Id)], CancellationToken.None).ConfigureAwait(false);
        OrphanScanner.Release(orphans);
        return await InitAndSaveAsync(storage, game, folders).ConfigureAwait(false);
    }

    private async Task<GameDefinition> ReconnectAsync(StorageSnapshot storage, GameDefinition game, IReadOnlyList<GameFolder> folders, OrphanResolution.Reconnect reconnect)
    {
        var repos = folders.Select(folder => GameFolders.Repo(storage, game.Id, folder)).ToList();

        // Only history that really is there under that id can be brought back.
        var branches = new HashSet<string>(StringComparer.Ordinal);
        var claimed = false;
        foreach (var (folder, repo) in folders.Zip(repos))
        {
            if (ConsentMarker.Read(folder.Path!) is ConsentMarkerState.Valid { Marker: var marker } && marker.GameId == game.Id)
            {
                claimed = true;
            }

            branches.UnionWith(await storage.Engine.ListBranchesAsync(repo).ConfigureAwait(false));
        }

        if (!claimed || branches.Count == 0)
        {
            throw new NoHistoryToReconnectException(game.Id);
        }

        var saved = await InitAndSaveAsync(storage, game, folders).ConfigureAwait(false);
        foreach (var recovered in reconnect.Profiles.Where(profile => branches.Contains(profile.BranchName)))
        {
            await _services.Catalog.SaveProfileAsync(await ProfileFromBranchAsync(game.Id, recovered).ConfigureAwait(false)).ConfigureAwait(false);
        }

        return saved;
    }

    // A recovered profile keeps its old id, unless another game's profile already holds it.
    private async Task<Profile> ProfileFromBranchAsync(string gameId, RecoveredProfile recovered)
    {
        var id = OrphanScanner.ProfileIdOf(recovered.BranchName);
        var taken = await _services.Catalog.GetProfileAsync(id).ConfigureAwait(false);
        if (taken is not null && taken.GameId != gameId)
        {
            id = $"{id}-{Guid.NewGuid().ToString("N")[..4]}";
        }

        return new Profile(Id: id, GameId: gameId, Name: recovered.Name, BranchName: recovered.BranchName);
    }
}
