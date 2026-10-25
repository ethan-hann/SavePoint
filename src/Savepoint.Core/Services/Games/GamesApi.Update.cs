using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Services.Games;

internal sealed partial class GamesApi
{
    public Task<OperationResult<GameDefinition>> UpdateAsync(GameDefinition definition, bool confirmFolderChange = false) =>
        ApiGuard.RunAsync(_services.Log, () => UpdateCoreAsync(definition, confirmFolderChange));

    private async Task<GameDefinition> UpdateCoreAsync(GameDefinition definition, bool confirmFolderChange)
    {
        var edited = CatalogValidator.RequireValid(definition);

        // The library key keeps two edits, or an edit and an add, from each passing the overlap check before the other's row is saved.
        return await _services.RunWithLocksAsync(
            [BackendServices.LibraryLockKey, edited.Id],
            () => _services.Storage.ReadAsync(async storage =>
            {
                var existing = await RequireGameAsync(edited.Id, CancellationToken.None).ConfigureAwait(false);
                var others = (await _services.Catalog.ListGamesAsync().ConfigureAwait(false)).Where(game => game.Id != existing.Id).ToList();
                var folders = GameFolders.ResolveStrictly(_services.PathTokens, edited);
                Vet(storage, edited, folders, others);

                var before = GameFolders.ResolveLoosely(_services.PathTokens, existing);

                // The session and its after-play save read the folders from the catalog later, so they must not move under it.
                if (_services.Sessions.IsGameRunning(existing.Id) && FoldersDiffer(before, folders))
                {
                    throw SwitchInProgressException.Refusal(_services.Sessions, existing.Id, existing.Name);
                }

                if (!confirmFolderChange)
                {
                    await RequireMatchingFoldersAsync(storage, existing.Id, before, folders, edited).ConfigureAwait(false);
                }

                var claimed = new List<GameFolder>();
                var made = new List<RepoRef>();
                try
                {
                    foreach (var folder in folders)
                    {
                        var old = before.FirstOrDefault(previous => previous.Role == folder.Role);
                        var repo = GameFolders.Repo(storage, edited.Id, folder);
                        if (old?.Path is null || !SamePath(old.Path, folder.Path!) || !Directory.Exists(repo.GitDir))
                        {
                            if (!Directory.Exists(repo.GitDir))
                            {
                                made.Add(repo);
                            }

                            if (ConsentMarker.Read(folder.Path!) is ConsentMarkerState.Missing)
                            {
                                claimed.Add(folder);
                            }

                            // Confirming the new place is what writes its marker; the history stays where it is, keyed by the game.
                            await storage.Engine.InitAsync(repo).ConfigureAwait(false);
                        }
                    }

                    // The ignore list is part of the edit: each folder's exclude file says what the saved game says before anything else reads it.
                    foreach (var folder in folders)
                    {
                        await storage.Engine.WriteExcludeAsync(GameFolders.Repo(storage, edited.Id, folder), edited.IgnorePathsFor(folder.Role), CancellationToken.None).ConfigureAwait(false);
                    }

                    var saved = await _services.Catalog.SaveGameAsync(edited).ConfigureAwait(false);
                    ReleaseOldFolders(existing.Id, before, folders);
                    return saved;
                }
                catch
                {
                    foreach (var folder in claimed)
                    {
                        TryRelease(folder.Path!, edited.Id, folder.Role);
                    }

                    await TakeBackAsync(storage, edited.Id, made).ConfigureAwait(false);
                    throw;
                }
            })).ConfigureAwait(false);
    }

    private bool FoldersDiffer(IReadOnlyList<GameFolder> before, IReadOnlyList<GameFolder> after) =>
        before.Count != after.Count
        || after.Any(folder => before.FirstOrDefault(previous => previous.Role == folder.Role) is not { Path: { } old } || !SamePath(old, folder.Path!));

    // A folder with history that is pointed somewhere else has to look like the folder it replaces: at least half of the files its
    // newest point recorded must be there at the same relative paths, or the player has most likely picked the wrong folder.
    // A file the ignore list names is not restored by a switch, so it is not one the new folder has to hold.
    private async Task RequireMatchingFoldersAsync(StorageSnapshot storage, string gameId, IReadOnlyList<GameFolder> before, IReadOnlyList<GameFolder> folders, GameDefinition edited)
    {
        foreach (var folder in folders)
        {
            var old = before.FirstOrDefault(previous => previous.Role == folder.Role);
            if (old?.Path is null || SamePath(old.Path, folder.Path!))
            {
                continue;
            }

            var recorded = await NewestPointFilesAsync(storage, GameFolders.Repo(storage, gameId, folder) with { IgnorePatterns = edited.IgnorePathsFor(folder.Role) }).ConfigureAwait(false);
            if (recorded.Count == 0)
            {
                continue;
            }

            var present = await Task.Run(() => recorded.Count(path => File.Exists(Path.Combine(folder.Path!, path.Replace('/', Path.DirectorySeparatorChar))))).ConfigureAwait(false);
            if (present * 2 < recorded.Count)
            {
                throw new FolderMismatchException(folder.Role, folder.Path!, present, recorded.Count);
            }
        }
    }

    // The files of the newest point on any branch of the folder's history; none when it has no point yet.
    private static async Task<IReadOnlyList<string>> NewestPointFilesAsync(StorageSnapshot storage, RepoRef repo)
    {
        CommitInfo? newest = null;
        foreach (var branch in await storage.Engine.ListBranchesAsync(repo).ConfigureAwait(false))
        {
            var tip = (await storage.Engine.ListCommitsAsync(repo, branch, 1).ConfigureAwait(false)).FirstOrDefault();
            if (tip is not null && (newest is null || tip.Date > newest.Date))
            {
                newest = tip;
            }
        }

        return newest is null ? [] : await storage.Engine.ListTreeFilesAsync(repo, newest.Id, leaveOutIgnored: true).ConfigureAwait(false);
    }

    private bool SamePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            _services.Platform.PathComparison);

    // A place the game no longer uses stops claiming it, so it can be added as a game of its own.
    private void ReleaseOldFolders(string gameId, IReadOnlyList<GameFolder> before, IReadOnlyList<GameFolder> after)
    {
        foreach (var old in before.Where(folder => folder.Path is not null))
        {
            var kept = after.Any(folder => folder.Role == old.Role && SamePath(folder.Path!, old.Path!));
            if (!kept)
            {
                TryRelease(old.Path!, gameId, old.Role);
            }
        }
    }

    // Only a marker that is this game's own is removed, and a place that cannot be reached is left as it is.
    private static void TryRelease(string folder, string gameId, RepoRole role)
    {
        try
        {
            ConsentMarker.Remove(folder, gameId, role);
        }
        catch (Exception e) when (e is SavepointException or IOException or UnauthorizedAccessException)
        {
            // A marker left behind is released the next time the folder is added, if no history stands behind it.
        }
    }
}
