using System.Collections.Concurrent;
using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Services;

namespace Savepoint.Core.Artwork;

// The real IArtworkApi. Reads are quick file reads; the Steam lookup runs in the background and tells the app through
// ArtworkChanged. Nothing here holds a lock across the network, and nothing here writes the catalog.
internal sealed class ArtworkApi : IArtworkApi
{
    // A miss is remembered, and a lookup made, for this long.
    private static readonly TimeSpan _missMemory = TimeSpan.FromDays(7);

    private readonly BackendServices _services;
    private readonly ArtworkStore _store;
    private readonly ISteamFolderLocator _steam;
    private readonly SteamCdn? _cdn;

    // One lookup per game at a time; a read that finds one running starts none.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _lookupGates = new(StringComparer.Ordinal);

    // When each (game, app id, online) lookup was last made in this run, so reads in a burst, and reads of a game with nothing to
    // find, do not repeat it. Misses that outlive a run are in the game's lookup file.
    private readonly ConcurrentDictionary<(string GameId, int? AppId, bool Online), DateTimeOffset> _lookedUp = new();

    private readonly ConcurrentDictionary<Guid, Task> _running = new();

    public ArtworkApi(BackendServices services, ISteamFolderLocator steam, HttpMessageHandler? http)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(steam);
        _services = services;
        _store = services.Artwork;
        _steam = steam;
        _cdn = http is null ? null : new SteamCdn(http, services.Time);
    }

    public event EventHandler<ArtworkChange>? ArtworkChanged;

    public Task<OperationResult<GameArtwork>> GetAsync(string gameId, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            async () =>
            {
                var game = await RequireGameAsync(gameId, cancellationToken).ConfigureAwait(false);
                var (id, detected) = await Task.Run(() => ResolveId(game), cancellationToken).ConfigureAwait(false);
                var read = await ReadAsync(game, id, detected, tidy: true).ConfigureAwait(false);
                if (await IsLookupDueAsync(read, id).ConfigureAwait(false))
                {
                    StartBackgroundLookup(game.Id);
                }

                return read.Art;
            },
            cancellationToken);

    public Task<OperationResult<GameArtwork>> SetCustomImageAsync(string gameId, string imagePath) =>
        ApiGuard.RunAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(imagePath);
            var game = await RequireGameAsync(gameId, CancellationToken.None).ConfigureAwait(false);
            using (await _store.EnterAsync(game.Id).ConfigureAwait(false))
            {
                await Task.Run(() => _store.SaveCustom(game.Id, imagePath)).ConfigureAwait(false);
            }

            Raise(game.Id);
            return await CurrentAsync(game).ConfigureAwait(false);
        });

    public Task<OperationResult<GameArtwork>> ClearCustomImageAsync(string gameId) =>
        ApiGuard.RunAsync(async () =>
        {
            var game = await RequireGameAsync(gameId, CancellationToken.None).ConfigureAwait(false);
            bool removed;
            using (await _store.EnterAsync(game.Id).ConfigureAwait(false))
            {
                removed = await Task.Run(() => _store.DeleteCustom(game.Id)).ConfigureAwait(false);
            }

            if (removed)
            {
                Raise(game.Id);
            }

            return await CurrentAsync(game).ConfigureAwait(false);
        });

    public Task<OperationResult<GameArtwork>> LookAgainAsync(string gameId) =>
        ApiGuard.RunAsync(async () =>
        {
            var game = await RequireGameAsync(gameId, CancellationToken.None).ConfigureAwait(false);
            var gate = LookupGateOf(game.Id);
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await LookUpAsync(game.Id, force: true).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }

            return await CurrentAsync(game).ConfigureAwait(false);
        });

    // Completes when no background lookup is running; tests wait on it.
    public async Task WhenIdleAsync()
    {
        while (_running.Values.ToArray() is { Length: > 0 } tasks)
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    private async Task<GameDefinition> RequireGameAsync(string gameId, CancellationToken cancellationToken) =>
        await _services.Catalog.GetGameAsync(gameId, cancellationToken).ConfigureAwait(false) ?? throw new GameNotFoundException(gameId);

    // The stored id wins; otherwise what the launch link or the folders say. File reads only.
    private (int? Id, bool Detected) ResolveId(GameDefinition game)
    {
        if (game.SteamAppId is { } stored)
        {
            return (stored, false);
        }

        var install = GameFolders.ResolveLoosely(_services.PathTokens, game).FirstOrDefault(folder => folder.Role == RepoRole.Install)?.Path;
        var detected = SteamAppIdDetector.Detect(game, install, _services.Platform.PathComparison);
        return (detected, detected is not null);
    }

    private async Task<GameArtwork> CurrentAsync(GameDefinition game)
    {
        var (id, detected) = await Task.Run(() => ResolveId(game)).ConfigureAwait(false);
        return (await ReadAsync(game, id, detected, tidy: true).ConfigureAwait(false)).Art;
    }

    private sealed record Read(GameArtwork Art, LookupState State, bool HasWide, bool HasSquare);

    // What is on disk for the game. With tidy, Steam images kept for another app id are removed first: they are another game's.
    private async Task<Read> ReadAsync(GameDefinition game, int? id, bool detected, bool tidy)
    {
        using var lease = await _store.EnterAsync(game.Id).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var state = _store.ReadState(game.Id);
            if (tidy && state.AppId != id && (_store.SteamPath(game.Id, ArtworkKind.Wide) is not null || _store.SteamPath(game.Id, ArtworkKind.Square) is not null))
            {
                _store.DeleteSteamImages(game.Id);
                state = new LookupState(null, null);
            }

            var custom = _store.CustomPath(game.Id);
            var wide = _store.SteamPath(game.Id, ArtworkKind.Wide);
            var square = _store.SteamPath(game.Id, ArtworkKind.Square);
            var art = custom is not null ? new GameArtwork(game.Id, ArtworkSource.Custom, custom, custom, id, detected)
                : wide is not null || square is not null ? new GameArtwork(game.Id, ArtworkSource.Steam, wide, square, id, detected)
                : new GameArtwork(game.Id, ArtworkSource.None, null, null, id, detected);
            return new Read(art, state, wide is not null, square is not null);
        }).ConfigureAwait(false);
    }

    private async Task<bool> IsLookupDueAsync(Read read, int? id)
    {
        // The game's own image makes Steam's unnecessary; a lookup waits until it is removed.
        if (read.Art.Source == ArtworkSource.Custom)
        {
            return false;
        }

        var now = _services.Time.GetUtcNow();
        var online = await IsOnlineAllowedAsync().ConfigureAwait(false);
        if (_lookedUp.TryGetValue((read.Art.GameId, id, online), out var at) && now - at < _missMemory)
        {
            return false;
        }

        if (id is not null && read.HasWide && read.HasSquare)
        {
            return false;
        }

        return !(read.State.AppId == id && read.State.MissAt is { } miss && now - miss < _missMemory);
    }

    // Lookups go online only with a handler, and only while the setting is on; a setting that cannot be read counts as on.
    private async Task<bool> IsOnlineAllowedAsync()
    {
        if (_cdn is null)
        {
            return false;
        }

        try
        {
            return (await _services.Catalog.GetSettingsAsync().ConfigureAwait(false))?.LookUpArtworkOnline ?? true;
        }
        catch (CatalogCorruptException)
        {
            return true;
        }
    }

    private SemaphoreSlim LookupGateOf(string gameId) => _lookupGates.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));

    private void StartBackgroundLookup(string gameId)
    {
        var gate = LookupGateOf(gameId);
        if (!gate.Wait(0))
        {
            return;
        }

        var key = Guid.NewGuid();
        _running[key] = Task.Run(async () =>
        {
            try
            {
                await LookUpAsync(gameId, force: false).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SavepointException or OperationCanceledException)
            {
                // Nothing is learned from a lookup that could not finish; the next one tries again.
            }
            finally
            {
                gate.Release();
                _running.TryRemove(key, out _);
            }
        });
    }

    // Finds the app id, takes images from the Steam client's cache, then from Steam's servers when allowed, and keeps what it found.
    // Without force, only the images not already kept are looked for; with it, everything is, and nothing found is thrown away.
    private async Task LookUpAsync(string gameId, bool force)
    {
        if (await _services.Catalog.GetGameAsync(gameId).ConfigureAwait(false) is not { } game)
        {
            return;
        }

        var (id, _) = await Task.Run(() => ResolveId(game)).ConfigureAwait(false);
        var online = await IsOnlineAllowedAsync().ConfigureAwait(false);
        var now = _services.Time.GetUtcNow();
        _lookedUp[(gameId, id, online)] = now;

        var found = new List<(ArtworkKind Kind, byte[] Bytes)>();
        var sawNotFound = false;
        var before = _store.ReadState(gameId);
        if (id is { } appId)
        {
            var steamFolder = _steam.FindSteamFolder();
            foreach (var kind in new[] { ArtworkKind.Wide, ArtworkKind.Square })
            {
                if (!force && before.AppId == appId && _store.SteamPath(gameId, kind) is not null)
                {
                    continue;
                }

                var bytes = steamFolder is null ? null : await Task.Run(() => FromSteamCache(steamFolder, appId, kind)).ConfigureAwait(false);
                if (bytes is null && online)
                {
                    var fetched = await _cdn!.FetchAsync(appId, kind, CancellationToken.None).ConfigureAwait(false);
                    bytes = fetched.Bytes;
                    sawNotFound |= fetched.Outcome == CdnOutcome.NotFound;
                }

                if (bytes is not null)
                {
                    found.Add((kind, bytes));
                }
            }
        }

        var changed = await CommitAsync(game, id, now, found, sawNotFound).ConfigureAwait(false);
        if (changed)
        {
            Raise(gameId);
        }
    }

    // Nothing bigger than a user's own image is copied out of Steam's folder, whatever it is called.
    private static byte[]? FromSteamCache(string steamFolder, int appId, ArtworkKind kind)
    {
        if (SteamLocalCache.Find(steamFolder, appId, kind) is not { } path)
        {
            return null;
        }

        try
        {
            return new FileInfo(path).Length is > 0 and <= ArtworkStore.MaxCustomBytes ? File.ReadAllBytes(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Writes the result under the game's lease, and only if the game still exists and still has the app id the lookup was for.
    private async Task<bool> CommitAsync(GameDefinition game, int? id, DateTimeOffset now, List<(ArtworkKind Kind, byte[] Bytes)> found, bool sawNotFound)
    {
        using var lease = await _store.EnterAsync(game.Id).ConfigureAwait(false);
        if (await _services.Catalog.GetGameAsync(game.Id).ConfigureAwait(false) is not { } current
            || (await Task.Run(() => ResolveId(current)).ConfigureAwait(false)).Id != id)
        {
            return false;
        }

        return await Task.Run(() =>
        {
            var changed = false;
            var state = _store.ReadState(game.Id);
            if (state.AppId != id && _store.DeleteSteamImages(game.Id))
            {
                changed = true;
                state = new LookupState(null, null);
            }

            foreach (var (kind, bytes) in found)
            {
                _store.SaveSteamImage(game.Id, kind, bytes);
                changed = true;
            }

            var complete = id is not null
                && _store.SteamPath(game.Id, ArtworkKind.Wide) is not null
                && _store.SteamPath(game.Id, ArtworkKind.Square) is not null;
            DateTimeOffset? missAt = id is null || sawNotFound ? now : complete || state.AppId != id ? null : state.MissAt;
            var next = new LookupState(id, missAt);
            if (next != _store.ReadState(game.Id))
            {
                _store.WriteState(game.Id, next);
            }

            return changed;
        }).ConfigureAwait(false);
    }

    private void Raise(string gameId) => ArtworkChanged?.Invoke(this, new ArtworkChange(gameId));
}
