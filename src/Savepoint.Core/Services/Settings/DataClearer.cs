using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Services.Settings;

/// <summary>
/// Removes everything SavePoint keeps and takes the app back to first run (D8): every repository and consent marker, the
/// catalog files, and SavePoint's own folders in the storage root. A game's own folders keep every file they hold; the
/// only thing removed from them is SavePoint's marker, and nothing is rolled back.
/// </summary>
internal sealed class DataClearer
{
    private readonly BackendServices _services;

    public DataClearer(BackendServices services)
    {
        _services = services;
    }

    public async Task<ClearAllDataPreview> PreviewAsync(CancellationToken cancellationToken)
    {
        var games = await _services.Catalog.ListGamesAsync(cancellationToken).ConfigureAwait(false);
        var profiles = await _services.Catalog.ListProfilesAsync(null, cancellationToken).ConfigureAwait(false);
        return await _services.Storage.ReadAsync(
            async storage =>
            {
                var listing = await Task.Run(() => StorageContent.Scan(storage.Root, cancellationToken), cancellationToken).ConfigureAwait(false);
                var markers = games
                    .SelectMany(game => GameFolders.ResolveLoosely(_services.PathTokens, game)
                        .Where(folder => folder.Path is not null && storage.Engine.GetConsentStatus(GameFolders.Repo(storage, game.Id, folder)).Confirmed)
                        .Select(folder => folder.Path!))
                    .ToList();
                var blocker = RunningBlocker(games);
                return new ClearAllDataPreview(
                    storage.Root,
                    listing.Bytes,
                    games.Count,
                    profiles.Count,
                    _services.Catalog.DirectoryPath,
                    markers,
                    blocker is null ? null : ErrorInfo.From(blocker));
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> StartAsync()
    {
        var games = await _services.Catalog.ListGamesAsync().ConfigureAwait(false);
        if (RunningBlocker(games) is { } blocker)
        {
            throw blocker;
        }

        // A clear cannot be stopped halfway: some games cleared and others not is worse than waiting for it to finish (D13.18).
        return _services.Jobs.Start(JobKind.ClearData, null, cancellable: false, _ => ClearAsync());
    }

    private SavepointException? RunningBlocker(IReadOnlyList<GameDefinition> games)
    {
        if (_services.Sessions.RunningGameIds().FirstOrDefault() is not { } running)
        {
            return null;
        }

        return new GameRunningException(running, games.FirstOrDefault(game => game.Id == running)?.Name ?? running);
    }

    private async Task<JobResult?> ClearAsync()
    {
        return await _services.Locks.RunExclusiveAsync(
            BackendServices.LibraryLockKey,
            async () =>
            {
                var games = await _services.Catalog.ListGamesAsync().ConfigureAwait(false);
                return await _services.RunWithLocksAsync(games.Select(game => game.Id), () => ClearLockedAsync(games)).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    private async Task<JobResult?> ClearLockedAsync(IReadOnlyList<GameDefinition> games)
    {
        if (RunningBlocker(games) is { } blocker)
        {
            throw blocker;
        }

        // Nothing reads or writes the storage while it is being taken apart.
        using var exclusive = await _services.Storage.Gate.EnterExclusiveAsync().ConfigureAwait(false);
        var storage = await _services.Storage.GetAsync().ConfigureAwait(false);
        var left = new Dictionary<string, ErrorInfo>(StringComparer.Ordinal);
        var listing = await Task.Run(() => StorageContent.Scan(storage.Root, CancellationToken.None)).ConfigureAwait(false);
        var bytes = listing.Bytes;

        foreach (var game in games)
        {
            foreach (var folder in GameFolders.ResolveLoosely(_services.PathTokens, game).Where(folder => folder.Path is not null))
            {
                await Attempt(left, folder.Path!, () => storage.Engine.DeleteRepoAsync(GameFolders.Repo(storage, game.Id, folder))).ConfigureAwait(false);
            }
        }

        // Whatever of SavePoint's is left in the root, including history of games the catalog no longer lists.
        foreach (var folder in listing.Folders)
        {
            await Attempt(left, folder, () => StorageFolders.DeleteAsync(storage, folder, CancellationToken.None)).ConfigureAwait(false);
        }

        foreach (var file in CatalogFiles())
        {
            await Attempt(left, file, () => Task.Run(() => DeleteFile(file))).ConfigureAwait(false);
        }

        // Artwork sits beside the catalog files, in the configuration folder, so it goes with them.
        await Attempt(left, _services.Artwork.Root, () => Task.Run(_services.Artwork.DeleteAll)).ConfigureAwait(false);

        _services.Storage.Reset();
        return new ClearAllDataResult(bytes, [.. left.Select(failure => new ClearFailure(failure.Key, failure.Value))]);
    }

    private IEnumerable<string> CatalogFiles()
    {
        if (!Directory.Exists(_services.Catalog.DirectoryPath))
        {
            return [];
        }

        string[] names = [CatalogStore.GamesFileName, CatalogStore.ProfilesFileName, CatalogStore.SettingsFileName];
        return Directory.EnumerateFiles(_services.Catalog.DirectoryPath)
            .Where(file => names.Any(name => Path.GetFileName(file).StartsWith(name, StringComparison.Ordinal)))
            .ToList();
    }

    private static void DeleteFile(string file)
    {
        if ((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        File.Delete(file);
    }

    // One thing that cannot be removed does not stop the rest; it is listed so the user can remove it by hand.
    private static async Task Attempt(Dictionary<string, ErrorInfo> left, string path, Func<Task> remove)
    {
        try
        {
            await remove().ConfigureAwait(false);
        }
        catch (Exception e) when (e is SavepointException or IOException or UnauthorizedAccessException)
        {
            // The error names the file that stayed in use when it knows; otherwise the folder being removed is what is listed.
            var error = ErrorInfo.From(e);
            var named = error.Args.TryGetValue("path", out var at) && at is string { Length: > 0 } where ? where : path;
            left.TryAdd(named, error);
        }
    }
}
