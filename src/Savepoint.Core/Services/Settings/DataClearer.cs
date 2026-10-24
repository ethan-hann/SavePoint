using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Jobs;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Services.Settings;

/// <summary>
/// Removes everything SavePoint keeps and takes the app back to first run (D8): every repository and consent marker, the
/// catalog files, and SavePoint's own folders in the storage root. A game's own folders keep every file they hold; the
/// only thing removed from them is SavePoint's marker, and nothing is rolled back.
/// </summary>
internal sealed class DataClearer
{
    // A process that was just stopped can hold a file of a git directory for a moment.
    private static readonly IReadOnlyList<TimeSpan> _removeDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1000),
    ];

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
        return _services.Jobs.Start(JobKind.ClearData, null, cancellable: false, ClearAsync);
    }

    private SavepointException? RunningBlocker(IReadOnlyList<GameDefinition> games)
    {
        if (_services.Sessions.RunningGameIds().FirstOrDefault() is not { } running)
        {
            return null;
        }

        return SwitchInProgressException.Refusal(_services.Sessions, running, games.FirstOrDefault(game => game.Id == running)?.Name ?? running);
    }

    private async Task<JobResult?> ClearAsync(JobContext context)
    {
        return await _services.Locks.RunExclusiveAsync(
            BackendServices.LibraryLockKey,
            async () =>
            {
                var games = await _services.Catalog.ListGamesAsync().ConfigureAwait(false);
                return await _services.RunWithLocksAsync(games.Select(game => game.Id), () => ClearLockedAsync(games, context)).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    private async Task<JobResult?> ClearLockedAsync(IReadOnlyList<GameDefinition> games, JobContext context)
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
        Log($"starting; the history of {games.Count} games and every leftover folder in {storage.Root} will go.");
        context.AddStep(JobSteps.ClearStart, ("games", games.Count));

        foreach (var game in games)
        {
            Log($"removing the history of game {game.Id}.");
            context.AddStep(JobSteps.ClearGame, ("game", game.Name));
            foreach (var folder in GameFolders.ResolveLoosely(_services.PathTokens, game).Where(folder => folder.Path is not null))
            {
                await Attempt(context, left, folder.Path!, () => storage.Engine.DeleteRepoAsync(GameFolders.Repo(storage, game.Id, folder))).ConfigureAwait(false);
            }
        }

        Log($"removing leftover history folders in {storage.Root} ({listing.Folders.Count} found).");
        context.AddStep(JobSteps.ClearFolders, ("count", listing.Folders.Count));

        // Whatever of SavePoint's is left in the root, including history of games the catalog no longer lists. Only git directories
        // SavePoint made are removed, and a folder that holds anything else stays, so a folder that merely looks like a game's is safe.
        foreach (var folder in listing.Folders)
        {
            foreach (var gitDir in new[] { Path.Combine(folder, "install.git"), Path.Combine(folder, "external.git") })
            {
                await Attempt(context, left, gitDir, () => GitDirRemoval.RemoveAsync(gitDir, storage.Root, storage.PathSafety.ResolveRealPath, _removeDelays, CancellationToken.None)).ConfigureAwait(false);
            }

            StorageFolders.RemoveIfEmpty(folder);
        }

        Log($"removing the catalog files in {_services.Catalog.DirectoryPath}.");
        context.AddStep(JobSteps.ClearSettings);
        foreach (var file in CatalogFiles())
        {
            await Attempt(context, left, file, () => Task.Run(() => DeleteFile(file))).ConfigureAwait(false);
        }

        // Artwork sits beside the catalog files, in the configuration folder, so it goes with them.
        Log("removing the artwork.");
        context.AddStep(JobSteps.ClearArtwork);
        await Attempt(context, left, _services.Artwork.Root, () => Task.Run(_services.Artwork.DeleteAll)).ConfigureAwait(false);

        var spared = SparedLogFile();
        Log($"emptying the log folder {_services.Platform.Directories.LogsDir}{(spared is null ? string.Empty : $", keeping {spared}, which is open")}.");
        context.AddStep(JobSteps.ClearLogs);
        foreach (var entry in LogFolderEntries(spared))
        {
            await Attempt(context, left, entry, () => Task.Run(() => DeleteEntry(entry))).ConfigureAwait(false);
        }

        _services.Storage.Reset();
        Log($"finished; {left.Count} items could not be removed.");
        context.AddStep(JobSteps.ClearDone, ("left", left.Count));
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

    private void Log(string step) => _services.Log.Information($"Clear all data: {step}");

    // The file the log has open is never removed: on Windows it cannot be, and elsewhere removing it would leave the log writing to a file nobody can see.
    private string? SparedLogFile() => _services.Log.CurrentFile is { Length: > 0 } current ? Path.GetFullPath(current) : null;

    private IEnumerable<string> LogFolderEntries(string? spared)
    {
        var logs = _services.Platform.Directories.LogsDir;
        if (!Directory.Exists(logs))
        {
            return [];
        }

        return Directory.EnumerateFileSystemEntries(logs)
            .Where(entry => spared is null || !string.Equals(Path.GetFullPath(entry), spared, _services.Platform.PathComparison))
            .ToList();
    }

    private static void DeleteEntry(string entry)
    {
        if (Directory.Exists(entry))
        {
            Directory.Delete(entry, recursive: true);
        }
        else
        {
            DeleteFile(entry);
        }
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
    private async Task Attempt(JobContext context, Dictionary<string, ErrorInfo> left, string path, Func<Task> remove)
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
            if (left.TryAdd(named, error))
            {
                _services.Log.Warning($"Clear all data: could not remove {named}: {error.MessageId}.", e);
                context.AddStep(JobSteps.ClearNotRemoved, ("path", named));
            }
        }
    }
}
