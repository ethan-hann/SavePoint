using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Backend;

/// <summary>
/// Everything that depends on where SavePoint's storage root is: the root itself, the path judge that protects it, and the
/// git engine built over that judge. A storage move replaces the snapshot as a whole (D13.4); operations read it once, after
/// taking their locks, and keep using that one for the whole call.
/// </summary>
public sealed class StorageSnapshot
{
    internal StorageSnapshot(string root, PathSafety pathSafety, GitEngine engine)
    {
        Root = root;
        PathSafety = pathSafety;
        Engine = engine;
    }

    /// <summary>The storage root, an absolute path.</summary>
    public string Root { get; }

    /// <summary>The path judge built for this root, with the platform's table and SavePoint's own folders protected.</summary>
    public PathSafety PathSafety { get; }

    /// <summary>The git engine whose writes are judged by <see cref="PathSafety"/>.</summary>
    public GitEngine Engine { get; }

    // The same storage for work that holds a game's lock, whose engine may repair a damaged index.
    internal StorageSnapshot ForGameLockHolder() => _lockHolder ??= new StorageSnapshot(Root, PathSafety, Engine.ForGameLockHolder());

    private StorageSnapshot? _lockHolder;

    /// <summary>The folder that holds both git directories of a game: <c>&lt;root&gt;/&lt;gameId&gt;</c>.</summary>
    /// <param name="gameId">The game's id.</param>
    /// <exception cref="ArgumentException">The id is not one the catalog accepts, so it could name a place outside the root: a rooted path, a separator, a dot segment.</exception>
    public string GameDirectory(string gameId) =>
        CatalogValidator.IsValidGameId(gameId)
            ? Path.Combine(Root, gameId)
            : throw new ArgumentException("A game id is a name in the storage root and cannot be a path.", nameof(gameId));

    /// <summary>The git directory of one folder of a game: <c>&lt;root&gt;/&lt;gameId&gt;/install.git</c> or <c>external.git</c>.</summary>
    /// <param name="gameId">The game's id.</param>
    /// <param name="role">Which of the game's folders.</param>
    /// <exception cref="ArgumentException">The id is not one the catalog accepts.</exception>
    public string GitDirectory(string gameId, RepoRole role) =>
        Path.Combine(GameDirectory(gameId), role == RepoRole.Install ? "install.git" : "external.git");

    // Waits for a reclaim a SavePoint that was killed left running in either folder of the game, before anything writes there.
    internal async Task SettleReclaimsAsync(string gameId, CancellationToken ct)
    {
        if (!CatalogValidator.IsValidGameId(gameId))
        {
            return;
        }

        await Engine.SettleReclaimAsync(GitDirectory(gameId, RepoRole.Install), ct).ConfigureAwait(false);
        await Engine.SettleReclaimAsync(GitDirectory(gameId, RepoRole.External), ct).ConfigureAwait(false);
    }
}
