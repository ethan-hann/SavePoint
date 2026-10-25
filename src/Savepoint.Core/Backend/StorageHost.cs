using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Backend;

/// <summary>
/// Owns the current <see cref="StorageSnapshot"/>: reads the storage root from the settings the first time it is needed,
/// builds the path judge and engine for it, and swaps them when the root moves. It is the only place a snapshot is made, so
/// the engine and the judge can never disagree about the root.
/// </summary>
public sealed class StorageHost
{
    // Unknown free space (a network share, say) is let through: the engine's own floor check is a courtesy, and refusing every write on such a volume would be worse.
    private const long _unknownFreeBytes = long.MaxValue / 4;

    private readonly CatalogStore _catalog;
    private readonly IPlatform _platform;
    private readonly GitRunner _runner;
    private readonly IDiskSpace _disk;
    private readonly string _installDirectory;
    private readonly GitEngineOptions? _engineOptions;
    private readonly SemaphoreSlim _build = new(1, 1);
    private volatile StorageSnapshot? _snapshot;

    // True once the root came from the saved settings (or a move): from then on a root that is not there is missing, not new.
    private volatile bool _rootSaved;

    internal StorageHost(
        CatalogStore catalog,
        IPlatform platform,
        GitRunner runner,
        IDiskSpace disk,
        string installDirectory,
        GitEngineOptions? engineOptions)
    {
        _catalog = catalog;
        _platform = platform;
        _runner = runner;
        _disk = disk;
        _installDirectory = installDirectory;
        _engineOptions = engineOptions;
    }

    internal StorageGate Gate { get; } = new();

    /// <summary>
    /// The snapshot for the storage root in the settings (the platform's default root when nothing is saved yet). Built once and
    /// kept until the root moves or the data is cleared.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait for the settings file.</param>
    /// <exception cref="CatalogCorruptException">The settings file cannot be read; nothing is built until it is recovered.</exception>
    /// <exception cref="StorageUnreachableException">The saved root is not an absolute path SavePoint can use, or it is not there (an unplugged or renamed drive); a root that was never saved is first made by the startup check (D31).</exception>
    public async Task<StorageSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_snapshot is { } ready)
        {
            return RequireReachable(ready);
        }

        await _build.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_snapshot is { } built)
            {
                return RequireReachable(built);
            }

            var settings = await _catalog.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            var saved = !string.IsNullOrWhiteSpace(settings?.StorageRoot);
            var snapshot = Build(saved ? settings!.StorageRoot : _platform.Directories.DefaultStorageRoot);
            _rootSaved = saved;
            _snapshot = snapshot;
            return RequireReachable(snapshot);
        }
        finally
        {
            _build.Release();
        }
    }

    /// <summary>Runs <paramref name="body"/> with the current snapshot while holding the storage gate shared, so the root cannot change under it.</summary>
    /// <typeparam name="T">What the body returns.</typeparam>
    /// <param name="body">The work; it must not keep the snapshot after it returns.</param>
    /// <param name="cancellationToken">Cancels the wait for the gate and for the settings.</param>
    public async Task<T> ReadAsync<T>(Func<StorageSnapshot, Task<T>> body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        using var lease = await Gate.EnterSharedAsync(cancellationToken).ConfigureAwait(false);
        return await body(await GetAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    // Builds the judge and engine for a root; throws when the root cannot be one.
    internal StorageSnapshot Build(string root)
    {
        if (!Path.IsPathFullyQualified(root))
        {
            throw new StorageUnreachableException(root, null);
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        PathSafety safety;
        try
        {
            safety = new PathSafety(_platform, full, [_installDirectory]);
        }
        catch (ArgumentException e)
        {
            throw new StorageUnreachableException(root, e);
        }

        var engine = new GitEngine(_runner, safety, directory => _disk.GetFreeBytes(directory) ?? _unknownFreeBytes, full, _engineOptions);
        return new StorageSnapshot(full, safety, engine);
    }

    // A saved root that is not there is a drive that went away, not an empty history; reading it as empty would let a move or a delete act on nothing.
    private StorageSnapshot RequireReachable(StorageSnapshot snapshot) =>
        !_rootSaved || Directory.Exists(snapshot.Root) ? snapshot : throw new StorageUnreachableException(snapshot.Root, null);

    // The startup check saved the root the snapshot was built for; from now on it is expected to be there.
    internal void MarkRootSaved() => _rootSaved = true;

    // The caller holds the gate exclusive. A root a move switched to is a saved one.
    internal void Replace(StorageSnapshot snapshot)
    {
        _rootSaved = true;
        _snapshot = snapshot;
        RootOverride = null;
    }

    // Forgets the snapshot so the next call reads the settings again; the caller holds the gate exclusive.
    internal void Reset()
    {
        _snapshot = null;
        RootOverride = null;
    }

    /// <summary>The folder in use for this run alone, when the settings could not be made to name it; null when the settings decide.</summary>
    internal string? RootOverride { get; private set; }

    // For the startup check, before anything has used the storage: history sits in a folder the settings cannot be made to name, and
    // showing it beats hiding it behind an empty default. Nothing is written, so the next start tries again.
    internal void UseForThisRun(string root)
    {
        var snapshot = Build(root);
        RootOverride = snapshot.Root;
        _rootSaved = true;
        _snapshot = snapshot;
    }
}
