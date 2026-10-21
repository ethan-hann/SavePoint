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
    /// <exception cref="StorageUnreachableException">The saved root is not an absolute path SavePoint can use.</exception>
    public async Task<StorageSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_snapshot is { } ready)
        {
            return ready;
        }

        await _build.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_snapshot is { } built)
            {
                return built;
            }

            var settings = await _catalog.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            var root = string.IsNullOrWhiteSpace(settings?.StorageRoot) ? _platform.Directories.DefaultStorageRoot : settings.StorageRoot;
            return _snapshot = Build(root);
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

        var engine = new GitEngine(_runner, safety, directory => _disk.GetFreeBytes(directory) ?? _unknownFreeBytes, _engineOptions);
        return new StorageSnapshot(full, safety, engine);
    }

    // The caller holds the gate exclusive.
    internal void Replace(StorageSnapshot snapshot) => _snapshot = snapshot;

    // Forgets the snapshot so the next call reads the settings again; the caller holds the gate exclusive.
    internal void Reset() => _snapshot = null;
}
