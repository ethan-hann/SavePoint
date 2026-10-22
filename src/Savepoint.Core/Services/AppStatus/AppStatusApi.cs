using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Services.AppStatus;

/// <summary>The real <see cref="IAppStatusApi"/>: the startup checks (D18) and catalog recovery (D13.2).</summary>
internal sealed class AppStatusApi : IAppStatusApi
{
    private const string _gitVersionPrefix = "git version ";

    private readonly BackendServices _services;
    private int _swept;

    public AppStatusApi(BackendServices services)
    {
        _services = services;
    }

    public Task<OperationResult<StartupStatus>> CheckAsync(CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                var git = await CheckGitAsync(cancellationToken).ConfigureAwait(false);
                var catalog = await CheckCatalogAsync(cancellationToken).ConfigureAwait(false);

                // The storage is only touched once the screens that explain a missing tool or a corrupt file are out of the way.
                if (git.Available && catalog.CorruptFiles.Count == 0)
                {
                    await PrepareStorageAsync(cancellationToken).ConfigureAwait(false);
                }

                return new StartupStatus(git, catalog);
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

    private async Task<GitStatus> CheckGitAsync(CancellationToken cancellationToken)
    {
        try
        {
            var version = await _services.Git.VerifyAsync(cancellationToken).ConfigureAwait(false);
            return new GitStatus(true, _services.Git.ExecutablePath, version.StartsWith(_gitVersionPrefix, StringComparison.Ordinal) ? version[_gitVersionPrefix.Length..] : version, null);
        }
        catch (GitUnavailableException e)
        {
            return new GitStatus(false, _services.Git.ExecutablePath, null, ErrorInfo.From(e));
        }
    }

    private async Task<CatalogStatus> CheckCatalogAsync(CancellationToken cancellationToken)
    {
        var corrupt = new List<CorruptCatalogFile>();
        await Probe(() => _services.Catalog.ListGamesAsync(cancellationToken)).ConfigureAwait(false);
        await Probe(() => _services.Catalog.ListProfilesAsync(null, cancellationToken)).ConfigureAwait(false);
        await Probe(() => _services.Catalog.GetSettingsAsync(cancellationToken)).ConfigureAwait(false);
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
    // unplugged drive or a renamed folder, and making an empty one in its place would hide the history behind it (D31).
    private async Task PrepareStorageAsync(CancellationToken cancellationToken)
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

        // A lock left by a run that was killed would refuse the first save; this is the one moment nothing of ours is running (D18).
        if (Interlocked.Exchange(ref _swept, 1) == 0)
        {
            foreach (var folder in StorageContent.Folders(storage.Root))
            {
                foreach (var gitDir in new[] { "install.git", "external.git" }.Select(name => Path.Combine(folder, name)).Where(Directory.Exists))
                {
                    storage.Engine.RemoveStaleLocks(gitDir);
                }
            }
        }
    }
}
