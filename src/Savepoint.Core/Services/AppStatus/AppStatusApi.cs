using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Services.AppStatus;

/// <summary>The real <see cref="IAppStatusApi"/>: the startup checks (D18) and catalog recovery (D13.2).</summary>
internal sealed class AppStatusApi : IAppStatusApi
{
    private const string _gitVersionPrefix = "git version ";

    private readonly BackendServices _services;
    private readonly Action<string>? _afterSettings;
    private int _swept;

    public AppStatusApi(BackendServices services, Action<string>? afterSettings = null)
    {
        _services = services;
        _afterSettings = afterSettings;
    }

    public Task<OperationResult<StartupStatus>> CheckAsync(CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                var git = await CheckGitAsync(cancellationToken).ConfigureAwait(false);
                var catalog = await CheckCatalogAsync(cancellationToken).ConfigureAwait(false);

                // The storage is only touched once the screens that explain a missing tool or a corrupt file are out of the way.
                var storage = git.Available && catalog.CorruptFiles.Count == 0
                    ? await PrepareStorageAsync(cancellationToken).ConfigureAwait(false)
                    : null;

                return new StartupStatus(git, catalog, storage);
            },
            cancellationToken);

    public Task<OperationResult<string>> RecoverCatalogFileAsync(string path, CatalogRecovery recovery, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                var backup = path + ".bak";
                string? restored = null;
                if (recovery == CatalogRecovery.RestoreBackup)
                {
                    // The copy is judged before anything moves, so a missing or broken backup leaves the file where it is.
                    if (!File.Exists(backup))
                    {
                        throw new InvalidOperationException($"'{path}' has no backup to restore.");
                    }

                    await RequireReadableAsync(backup, cancellationToken).ConfigureAwait(false);

                    // Made next to the target before the corrupt file moves, so a copy that fails never leaves the catalog missing.
                    restored = $"{path}.{Guid.NewGuid():N}.tmp";
                    File.Copy(backup, restored);
                }

                try
                {
                    var moved = await _services.Catalog.ReplaceCorruptFileAsync(path, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException($"'{path}' is not corrupt.");
                    if (restored is not null)
                    {
                        PutInPlace(restored, path, moved);
                        restored = null;
                    }

                    return moved;
                }
                finally
                {
                    if (restored is not null)
                    {
                        TryDelete(restored);
                    }
                }
            },
            cancellationToken);

    public Task<OperationResult<string>> UseStorageFolderAsync(string folder, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                var root = _services.PathTokens.Resolve(folder, "storageRoot");
                await RequireStorageLostAsync(cancellationToken).ConfigureAwait(false);
                var games = await _services.Catalog.ListGamesAsync(cancellationToken).ConfigureAwait(false);

                // Judged before anything changes: a folder with no history of these games is the wrong one, or a drive that came back empty.
                // With no games listed, any game folder SavePoint made counts.
                var holdsHistory = games.Count > 0
                    ? games.Any(game => StorageContent.HoldsStampedGitDirectory(Path.Combine(root, game.Id)))
                    : StorageContent.Folders(root).Any(StorageContent.HoldsStampedGitDirectory);
                if (!Directory.Exists(root) || !holdsHistory)
                {
                    throw new StorageHistoryNotFoundException(root);
                }

                // The folder is judged as a move's destination is: not inside the installer's folder, not over a game's folders, not what an unfinished move left.
                var pathSafety = _services.Storage.Build(root).PathSafety;
                if (StorageDestination.Refusal(_services, pathSafety, games, root) is { } refusal)
                {
                    throw refusal;
                }

                // A link would lead the cleanup below, and every later write, to files that are not SavePoint's; nothing is written until none is found.
                if (StorageFolderLinks.Refusal(root, games, pathSafety.ResolveRealPath) is { } linked)
                {
                    throw linked;
                }

                // A folder someone else prepared can carry a config or a hook that names a program; nothing reads it before this.
                foreach (var gitDir in StorageContent.Folders(root).SelectMany(folder => new[] { "install.git", "external.git" }.Select(name => Path.Combine(folder, name))).Where(Directory.Exists))
                {
                    AdoptedGitDirectory.MakeSafe(gitDir);
                }

                return await SwitchStorageAsync(root, create: false, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    public Task<OperationResult<string>> StartWithEmptyStorageAsync(CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                await RequireStorageLostAsync(cancellationToken).ConfigureAwait(false);
                var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_services.Platform.Directories.DefaultStorageRoot));

                // History already in the default folder is not an empty start; "Find the folder" is how to use it. Empty repositories, and
                // what an earlier start left half made, are not history, so a start that failed can be made again.
                if (Directory.Exists(root) && StorageContent.Folders(root).Any(StorageContent.HoldsHistory))
                {
                    throw new StorageDefaultFolderHasHistoryException(root);
                }

                return await SwitchStorageAsync(root, create: true, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    // Both choices are for a folder that is gone; with the saved folder in reach there is nothing to find or start over from.
    private async Task RequireStorageLostAsync(CancellationToken cancellationToken)
    {
        StorageSnapshot reachable;
        try
        {
            reachable = await _services.Storage.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (StorageUnreachableException)
        {
            return;
        }

        throw new StorageStillReachableException(reachable.Root);
    }

    // The library key, then every game's key, then the storage gate: the order everything that changes the root takes them in. Nothing
    // else reads or writes the storage while the root changes. Every listed game gets its repositories in the new root before the
    // settings change, so a failure up to there leaves the app on the root it had; a root change that leaves a game without them would
    // leave every save of it failing.
    private Task<string> SwitchStorageAsync(string root, bool create, CancellationToken cancellationToken) =>
        _services.Locks.RunExclusiveAsync(
            BackendServices.LibraryLockKey,
            async () =>
            {
                var games = await _services.Catalog.ListGamesAsync(cancellationToken).ConfigureAwait(false);
                return await _services.RunWithLocksAsync(
                    games.Select(game => game.Id),
                    async () =>
                    {
                        using var exclusive = await _services.Storage.Gate.EnterExclusiveAsync(cancellationToken).ConfigureAwait(false);

                        // The check made before the queue is stale by now: another choice, or the drive coming back, may have changed the root.
                        await RequireStorageLostAsync(cancellationToken).ConfigureAwait(false);
                        var snapshot = _services.Storage.Build(root);
                        if (create)
                        {
                            try
                            {
                                Directory.CreateDirectory(snapshot.Root);
                            }
                            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
                            {
                                throw new StorageUnreachableException(snapshot.Root, e);
                            }
                        }

                        await GiveRepositoriesAsync(snapshot, games, cancellationToken).ConfigureAwait(false);
                        await StorageRootSetting.CommitAsync(_services, snapshot.Root, _afterSettings).ConfigureAwait(false);
                        _services.Storage.Replace(snapshot);
                        _services.Log.Information($"Storage: now using {snapshot.Root}, chosen at startup.");
                        return snapshot.Root;
                    },
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    // An empty repository for each folder of each listed game that has none in the new root and whose folder confirms it belongs to that
    // game. A folder that cannot take one (gone, refused by the path rules, claimed by another game) is left for the editor, which makes
    // the repository when its folder is set again; it must not stop the others from being usable.
    private async Task GiveRepositoriesAsync(StorageSnapshot snapshot, IReadOnlyList<GameDefinition> games, CancellationToken cancellationToken)
    {
        foreach (var game in games)
        {
            IReadOnlyList<GameFolder> folders;
            try
            {
                folders = GameFolders.ResolveStrictly(_services.PathTokens, game);
            }
            catch (ValidationException e)
            {
                _services.Log.Warning($"Storage: {game.Id} has a folder this system cannot resolve, so it got no repository in {snapshot.Root}.", e);
                continue;
            }

            foreach (var folder in folders)
            {
                var repo = GameFolders.Repo(snapshot, game.Id, folder);
                if (!NeedsRepository(repo.GitDir) || !snapshot.Engine.GetConsentStatus(repo).Confirmed)
                {
                    continue;
                }

                try
                {
                    await snapshot.Engine.InitAsync(repo, ct: cancellationToken).ConfigureAwait(false);
                }
                catch (SavepointException e)
                {
                    _services.Log.Warning($"Storage: {game.Id} got no repository for its {folder.Role} folder in {snapshot.Root}: {ErrorInfo.From(e).MessageId}.", e);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // The drive is full or read-only: no other folder fares better, and the choice is not made, so it can be made again.
                    var gameFolder = Path.GetDirectoryName(repo.GitDir)!;
                    _services.Log.Warning($"Storage: {game.Id} could not be given a repository for its {folder.Role} folder in {snapshot.Root}.", e);
                    throw new StorageWriteFailedException(gameFolder, $"'{gameFolder}' could not be written to: {e.Message}", e);
                }
            }
        }
    }

    // A repository with a branch is history and is never touched. One without is made, or finished when an earlier start stopped
    // after git made it and before SavePoint wrote its settings.
    private static bool NeedsRepository(string gitDir) =>
        !File.Exists(Path.Combine(gitDir, "HEAD")) || (!StorageContent.HasBranch(gitDir) && !RepoConfigFile.IsCurrent(gitDir));

    private async Task<GitStatus> CheckGitAsync(CancellationToken cancellationToken)
    {
        try
        {
            var version = await _services.Git.VerifyAsync(cancellationToken).ConfigureAwait(false);
            var shown = version.StartsWith(_gitVersionPrefix, StringComparison.Ordinal) ? version[_gitVersionPrefix.Length..] : version;
            _services.Log.Information($"Startup check: git {shown} at {_services.Git.ExecutablePath}.");
            return new GitStatus(true, _services.Git.ExecutablePath, shown, null);
        }
        catch (GitUnavailableException e)
        {
            _services.Log.Error($"Startup check: git is not available at {_services.Git.ExecutablePath}: {ErrorInfo.From(e).MessageId}.", e);
            return new GitStatus(false, _services.Git.ExecutablePath, null, ErrorInfo.From(e));
        }
    }

    private async Task<CatalogStatus> CheckCatalogAsync(CancellationToken cancellationToken)
    {
        var corrupt = new List<CorruptCatalogFile>();
        await Probe(() => _services.Catalog.ListGamesAsync(cancellationToken)).ConfigureAwait(false);
        await Probe(() => _services.Catalog.ListProfilesAsync(null, cancellationToken)).ConfigureAwait(false);
        await Probe(() => _services.Catalog.GetSettingsAsync(cancellationToken)).ConfigureAwait(false);
        if (corrupt.Count == 0)
        {
            _services.Log.Information($"Startup check: the catalog in {_services.Catalog.DirectoryPath} is readable.");
        }

        foreach (var file in corrupt)
        {
            var backup = file.BackupPath is { } good ? $"a good backup is at {good}" : "there is no backup";
            _services.Log.Warning($"Startup check: {file.Path} cannot be read ({file.Error.MessageId}); {backup}.");
        }

        return new CatalogStatus(_services.Catalog.DirectoryPath, corrupt);

        async Task Probe<T>(Func<Task<T>> read)
        {
            try
            {
                await read().ConfigureAwait(false);
            }
            catch (CatalogCorruptException e)
            {
                var file = (string)e.Args["path"]!;
                corrupt.Add(new CorruptCatalogFile(file, await GoodBackupAsync(file, cancellationToken).ConfigureAwait(false), ErrorInfo.From(e)));
            }
        }
    }

    // The last good copy (D13.2), offered only when it can really be read back.
    private async Task<string?> GoodBackupAsync(string file, CancellationToken cancellationToken)
    {
        var backup = file + ".bak";
        if (!File.Exists(backup))
        {
            return null;
        }

        try
        {
            await RequireReadableAsync(backup, cancellationToken).ConfigureAwait(false);
            return backup;
        }
        catch (CatalogCorruptException)
        {
            return null;
        }
    }

    // Reads the copy through a catalog store of its own, in a scratch folder, so the real catalog is never involved.
    private async Task RequireReadableAsync(string backup, CancellationToken cancellationToken)
    {
        var scratch = Path.Combine(_services.Platform.Directories.RuntimeDir, "catalog-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            var name = Path.GetFileName(backup[..^".bak".Length]);
            Directory.CreateDirectory(scratch);
            File.Copy(backup, Path.Combine(scratch, name));
            var probe = new CatalogStore(scratch);
            switch (name)
            {
                case CatalogStore.GamesFileName:
                    await probe.ListGamesAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case CatalogStore.ProfilesFileName:
                    await probe.ListProfilesAsync(null, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    await probe.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (CatalogCorruptException)
        {
            throw new CatalogCorruptException(backup, null);
        }
        finally
        {
            StorageFolders.DeleteTree(scratch);
        }
    }

    // From now on the root counts as saved: a missing folder is reported, not made again. The next start tries again if this write fails.
    private async Task RememberRootAsync(string root, CancellationToken cancellationToken)
    {
        try
        {
            await _services.Catalog.UpdateSettingsAsync(
                current => (current ?? AppSettings.Defaults(root)) with { StorageRoot = root },
                cancellationToken).ConfigureAwait(false);
            _services.Storage.MarkRootSaved();
        }
        catch (Exception e) when (e is SavepointException or IOException or UnauthorizedAccessException)
        {
            // Left unsaved; the root is the default one and is made again, empty, only while no settings name one.
        }
    }

    // The finished copy is renamed into place, so a crash leaves no half-restored file; if that fails the corrupt file goes back, so the user is where they started.
    private static void PutInPlace(string copy, string target, string movedAside)
    {
        try
        {
            File.Move(copy, target, overwrite: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Move(movedAside, target, overwrite: false);
            }
            catch (Exception back) when (back is IOException or UnauthorizedAccessException)
            {
                // The corrupt file stays under its new name, which the error names through the folder it is in.
            }

            throw;
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A stray temp file is swept by the next write of this catalog file.
        }
    }

    // The storage folder is made only the first time, when no root has been saved. After that a folder that is not there is an
    // unplugged drive or a renamed folder, and making an empty one in its place would hide the history behind it (D31). Such a
    // folder is a finding, not a failed check: the startup screen offers a way on (D37).
    private async Task<StorageStatus> PrepareStorageAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _services.Catalog.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            var firstRun = string.IsNullOrWhiteSpace(settings?.StorageRoot);
            var storage = await _services.Storage.GetAsync(cancellationToken).ConfigureAwait(false);
            if (firstRun)
            {
                try
                {
                    Directory.CreateDirectory(storage.Root);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    throw new StorageUnreachableException(storage.Root, e);
                }

                await RememberRootAsync(storage.Root, cancellationToken).ConfigureAwait(false);
            }

            var found = firstRun ? "was created for the first run" : Directory.Exists(storage.Root) ? "was found" : "was not found";
            _services.Log.Information($"Startup check: storage folder {storage.Root} {found}.");

            // A lock left by a run that was killed would refuse the first save; this is the one moment nothing of ours is running (D18).
            if (Interlocked.Exchange(ref _swept, 1) == 0)
            {
                foreach (var folder in StorageContent.Folders(storage.Root))
                {
                    foreach (var gitDir in new[] { "install.git", "external.git" }.Select(name => Path.Combine(folder, name)).Where(Directory.Exists))
                    {
                        var removed = storage.Engine.RemoveStaleLocks(gitDir);
                        if (removed > 0)
                        {
                            _services.Log.Information($"Startup check: removed {removed} leftover lock or temporary files from {gitDir}.");
                        }
                    }
                }
            }

            return new StorageStatus(true, storage.Root, null);
        }
        catch (StorageUnreachableException e)
        {
            var root = e.Args.GetValueOrDefault("path") as string ?? string.Empty;
            _services.Log.Warning($"Startup check: storage folder {root} cannot be reached.", e);
            return new StorageStatus(false, root, ErrorInfo.From(e));
        }
    }
}
