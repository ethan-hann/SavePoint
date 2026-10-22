using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Services.Games;

internal sealed partial class GamesApi
{
    public Task<OperationResult<AddGameOutcome>> AddAsync(GameDefinition definition) =>
        ApiGuard.RunAsync(() => AddCoreAsync(definition));

    private async Task<AddGameOutcome> AddCoreAsync(GameDefinition definition)
    {
        var game = CatalogValidator.RequireValid(definition);

        // The library key keeps a storage move from running while a new game's repositories are being made under the old root.
        return await _services.RunWithLocksAsync(
            [BackendServices.LibraryLockKey, game.Id],
            () => _services.Storage.ReadAsync<AddGameOutcome>(async storage =>
            {
                var games = await _services.Catalog.ListGamesAsync().ConfigureAwait(false);
                if (games.Any(existing => existing.Id == game.Id))
                {
                    throw new GameAlreadyExistsException(game.Id);
                }

                var folders = GameFolders.ResolveStrictly(_services.PathTokens, game);
                Vet(storage, game, folders, games);

                var orphans = await OrphanScanner.FindAsync(storage, game.Id, folders, [.. games.Select(g => g.Id)], CancellationToken.None).ConfigureAwait(false);
                if (orphans.Any(orphan => orphan.HasHistory))
                {
                    return new AddGameOutcome.HistoryFound(OrphanScanner.Describe(orphans));
                }

                // A marker with no history behind it protects nothing; it is let go so the folder can be claimed.
                OrphanScanner.Release(orphans);
                return new AddGameOutcome.Added(await InitAndSaveAsync(storage, game, folders).ConfigureAwait(false));
            }));
    }

    // Every check that can refuse a game before anything is written, each placed on the folder it is about.
    private void Vet(StorageSnapshot storage, GameDefinition game, IReadOnlyList<GameFolder> folders, IReadOnlyList<GameDefinition> others)
    {
        foreach (var folder in folders)
        {
            try
            {
                storage.PathSafety.Check(folder.Path!);
            }
            catch (PathSafetyException refused)
            {
                throw new FolderErrorException(refused, folder.Role);
            }
        }

        if (folders.Count == 2 && storage.PathSafety.PathsOverlap(folders[0].Path!, folders[1].Path!))
        {
            throw new ValidationException([new ValidationIssue("external.path", ValidationMessageIds.GamePathsNested)]);
        }

        foreach (var other in others.Where(existing => existing.Id != game.Id))
        {
            var theirs = GameFolders.ResolveLoosely(_services.PathTokens, other).Where(folder => folder.Path is not null).ToList();
            foreach (var mine in folders)
            {
                if (theirs.Any(folder => storage.PathSafety.PathsOverlap(mine.Path!, folder.Path!)))
                {
                    throw new FolderErrorException(new GameOverlapException(other.Id, other.Name), mine.Role);
                }
            }
        }
    }

    // Makes the repositories (which writes the consent markers) and then the catalog row; a failure on the way takes back what this call made.
    private async Task<GameDefinition> InitAndSaveAsync(StorageSnapshot storage, GameDefinition game, IReadOnlyList<GameFolder> folders)
    {
        var made = new List<RepoRef>();
        try
        {
            foreach (var folder in folders)
            {
                var repo = GameFolders.Repo(storage, game.Id, folder);
                if (!Directory.Exists(repo.GitDir))
                {
                    made.Add(repo);
                }

                await storage.Engine.InitAsync(repo).ConfigureAwait(false);
            }

            return await _services.Catalog.SaveGameAsync(game).ConfigureAwait(false);
        }
        catch
        {
            await TakeBackAsync(storage, game.Id, made).ConfigureAwait(false);
            throw;
        }
    }

    // Best effort: the original failure is the one worth reporting.
    private static async Task TakeBackAsync(StorageSnapshot storage, string gameId, IEnumerable<RepoRef> made)
    {
        foreach (var repo in made.Reverse())
        {
            try
            {
                await storage.Engine.DeleteRepoAsync(repo).ConfigureAwait(false);
            }
            catch (Exception e) when (e is SavepointException or IOException or UnauthorizedAccessException)
            {
                // What could not be removed is found and removed by the next add of this game.
            }
        }

        try
        {
            var folder = storage.GameDirectory(gameId);
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An empty folder left behind is harmless.
        }
    }
}
