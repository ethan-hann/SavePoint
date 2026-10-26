using Savepoint.Core.Artwork;
using Savepoint.Core.Catalog;
using Savepoint.Core.Diagnostics;
using Savepoint.Core.Git;
using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Services;
using Savepoint.Core.Services.Exe;
using Savepoint.Core.Services.Jobs;
using Savepoint.Core.Services.Locking;
using Savepoint.Core.Services.Profiles;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Backend;

/// <summary>
/// The pieces every part of the backend shares, built once for the life of the process. The launch component is built from
/// this, so it works with the same catalog, job registry, game lock and storage as the services do.
/// </summary>
/// <remarks>
/// Nothing here is rebuilt when the storage root moves except what <see cref="Storage"/> hands out as a snapshot (D13.4): the
/// job registry, the game lock and the session state live as long as the process.
/// </remarks>
public sealed class BackendServices
{
    /// <summary>The lock key the services take, ahead of any game's own key, for work that adds or removes games.</summary>
    public const string LibraryLockKey = "*library*";

    internal BackendServices(
        IPlatform platform,
        CatalogStore catalog,
        GitBinary git,
        GitRunner runner,
        JobRegistry jobs,
        GameLock locks,
        IGameSessions sessions,
        StorageHost storage,
        IDiskSpace disk,
        TimeProvider time,
        string installDirectory,
        int looseObjectThreshold,
        IDiagnosticLog log)
    {
        Log = log;
        Platform = platform;
        Catalog = catalog;
        Git = git;
        Runner = runner;
        Jobs = jobs;
        Locks = locks;
        Sessions = sessions;
        Storage = storage;
        Disk = disk;
        Time = time;
        InstallDirectory = installDirectory;
        LooseObjectThreshold = looseObjectThreshold;
        PathTokens = new PathTokenResolver(platform);
        Executables = new ExecutableScanner(platform);
        Insights = new GameInsights(this);
        Artwork = new ArtworkStore(catalog.DirectoryPath);
        Capture = new CaptureRunner(this, new OptimizationService(this));
    }

    /// <summary>Where the backend records what happened (D34). Never null, and never throws: a failing log is swallowed.</summary>
    public IDiagnosticLog Log { get; }

    /// <summary>The operating system's folders, path tokens and safety table.</summary>
    public IPlatform Platform { get; }

    /// <summary>The games, profiles and settings files.</summary>
    public CatalogStore Catalog { get; }

    /// <summary>The bundled git the engine runs.</summary>
    public GitBinary Git { get; }

    /// <summary>Runs git processes; the engine is built over it.</summary>
    public GitRunner Runner { get; }

    /// <summary>Runs, tracks and cancels long work.</summary>
    public JobRegistry Jobs { get; }

    /// <summary>Orders git work per game; never held across a play session.</summary>
    public GameLock Locks { get; }

    /// <summary>Which games and profiles are being played.</summary>
    public IGameSessions Sessions { get; }

    /// <summary>The storage root and the engine built for it.</summary>
    public StorageHost Storage { get; }

    /// <summary>Free and total space of a volume.</summary>
    public IDiskSpace Disk { get; }

    /// <summary>The clock for timestamps the services write.</summary>
    public TimeProvider Time { get; }

    /// <summary>The folder SavePoint itself is installed in, which no game may overlap.</summary>
    public string InstallDirectory { get; }

    /// <summary>How many loose objects in a repository make a capture queue an optimization (D13.17).</summary>
    public int LooseObjectThreshold { get; }

    /// <summary>Expands path tokens such as <c>{documents}</c> for this platform.</summary>
    public PathTokenResolver PathTokens { get; }

    /// <summary>Finds the files in an install folder that could launch the game.</summary>
    public ExecutableScanner Executables { get; }

    internal GameInsights Insights { get; }

    // Where artwork is kept, in the configuration folder: a storage move never touches it, and deleting a game or all data removes it.
    internal ArtworkStore Artwork { get; }

    // The one place a profile's folders are saved as a capture event, so its memory of half-finished saves has one owner.
    internal CaptureRunner Capture { get; }

    /// <summary>Remembers detected exe versions by the content id of the executable (D13.15).</summary>
    public ExeVersionCache ExeVersions { get; } = new();

    /// <summary>
    /// Runs <paramref name="body"/> holding the game's lock, then the storage gate shared, with the snapshot read after both are
    /// held. Every write to a game's git directories goes through here.
    /// </summary>
    /// <typeparam name="T">What the body returns.</typeparam>
    /// <param name="gameId">The game whose lock to take.</param>
    /// <param name="body">The work; the handle lets it allow a later request to preempt it.</param>
    /// <param name="cancellationToken">Abandons the wait for the lock; it has no effect once the body has started.</param>
    public Task<T> RunForGameAsync<T>(string gameId, Func<StorageSnapshot, GameLockHandle, Task<T>> body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gameId);
        ArgumentNullException.ThrowIfNull(body);
        return Locks.RunExclusiveAsync(
            gameId,
            async handle =>
            {
                using var lease = await Storage.Gate.EnterSharedAsync(cancellationToken).ConfigureAwait(false);
                var storage = (await Storage.GetAsync(cancellationToken).ConfigureAwait(false)).ForGameLockHolder();

                // A reclaim that outlived a SavePoint that was killed would delete packs a write relies on.
                await storage.SettleReclaimsAsync(gameId, cancellationToken).ConfigureAwait(false);
                return await body(storage, handle).ConfigureAwait(false);
            },
            cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="body"/> holding the lock of every key, taken in sorted order so two such calls can never wait on
    /// each other. A storage move, a clear-all and anything else that touches every game uses it.
    /// </summary>
    /// <typeparam name="T">What the body returns.</typeparam>
    /// <param name="keys">The lock keys, usually <see cref="LibraryLockKey"/> and every game id.</param>
    /// <param name="body">The work, started once every lock is held.</param>
    /// <param name="cancellationToken">Abandons the wait for the locks.</param>
    public Task<T> RunWithLocksAsync<T>(IEnumerable<string> keys, Func<Task<T>> body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(body);
        var ordered = keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return TakeAsync(0);

        Task<T> TakeAsync(int index) =>
            index == ordered.Length
                ? body()
                : Locks.RunExclusiveAsync(ordered[index], () => TakeAsync(index + 1), cancellationToken);
    }
}
