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
    private readonly IGogGalaxyCacheLocator _gog;
    private readonly SteamCdn? _cdn;

    // One lookup per game at a time; a read that finds one running starts none.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _lookupGates = new(StringComparer.Ordinal);

    // When each (game, app id, online) lookup last came to an answer in this run (found, not found, or no id to look for), so reads in a
    // burst, and reads of a game with nothing to find, do not repeat it. A lookup that failed leaves nothing here, and is tried again
    // at the next read. Misses that outlive a run are in the game's lookup file.
    private readonly ConcurrentDictionary<(string GameId, int? AppId, string? GogId, bool Online), DateTimeOffset> _lookedUp = new();

    // The last failure written to the log for each game, so a failure that repeats on every read is not written every time.
    private readonly ConcurrentDictionary<string, string> _loggedFailures = new(StringComparer.Ordinal);

    public ArtworkApi(BackendServices services, ISteamFolderLocator steam, IGogGalaxyCacheLocator gog, HttpMessageHandler? http)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(steam);
        ArgumentNullException.ThrowIfNull(gog);
        _services = services;
        _store = services.Artwork;
        _steam = steam;
        _gog = gog;
        _cdn = http is null ? null : new SteamCdn(http, services.Time);
    }

    public event EventHandler<ArtworkChange>? ArtworkChanged;

    public Task<OperationResult<GameArtwork>> GetAsync(string gameId, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                var game = await RequireGameAsync(gameId, cancellationToken).ConfigureAwait(false);
                var ids = await Task.Run(() => ResolveIds(game), cancellationToken).ConfigureAwait(false);
                var read = await ReadAsync(game, ids, tidy: true).ConfigureAwait(false);
                if (await IsLookupDueAsync(read, ids).ConfigureAwait(false))
                {
                    StartBackgroundLookup(game.Id);
                }

                return read.Art;
            },
            cancellationToken);

    public Task<OperationResult<GameArtwork>> SetCustomImageAsync(string gameId, string imagePath) =>
        ApiGuard.RunAsync(_services.Log, async () =>
        {
            ArgumentNullException.ThrowIfNull(imagePath);
            var game = await RequireGameAsync(gameId, CancellationToken.None).ConfigureAwait(false);
            using (await _store.EnterAsync(game.Id).ConfigureAwait(false))
            {
                // The game may have been deleted while this waited for the lease; a folder made now would outlive it.
                _ = await RequireGameAsync(game.Id, CancellationToken.None).ConfigureAwait(false);
                await Task.Run(() => _store.SaveCustom(game.Id, imagePath)).ConfigureAwait(false);
            }

            Raise(game.Id);
            return await CurrentAsync(game).ConfigureAwait(false);
        });

    public Task<OperationResult<GameArtwork>> ClearCustomImageAsync(string gameId) =>
        ApiGuard.RunAsync(_services.Log, async () =>
        {
            var game = await RequireGameAsync(gameId, CancellationToken.None).ConfigureAwait(false);
            bool removed;
            using (await _store.EnterAsync(game.Id).ConfigureAwait(false))
            {
                _ = await RequireGameAsync(game.Id, CancellationToken.None).ConfigureAwait(false);
                removed = await Task.Run(() => _store.DeleteCustom(game.Id)).ConfigureAwait(false);
            }

            if (removed)
            {
                Raise(game.Id);
            }

            return await CurrentAsync(game).ConfigureAwait(false);
        });

    public Task<OperationResult<GameArtwork>> LookAgainAsync(string gameId) =>
        ApiGuard.RunAsync(_services.Log, async () =>
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

    private async Task<GameDefinition> RequireGameAsync(string gameId, CancellationToken cancellationToken) =>
        await _services.Catalog.GetGameAsync(gameId, cancellationToken).ConfigureAwait(false) ?? throw new GameNotFoundException(gameId);

    // What a game's artwork is looked up by: the Steam app id (the stored one wins; otherwise what the launch link or the folders say) and the
    // GOG product id the install folder's info files name. File reads only.
    private readonly record struct Ids(int? AppId, bool Detected, string? GogId);

    private Ids ResolveIds(GameDefinition game)
    {
        var install = GameFolders.ResolveLoosely(_services.PathTokens, game).FirstOrDefault(folder => folder.Role == RepoRole.Install)?.Path;
        var gog = GogProductIdDetector.Detect(install);
        if (game.SteamAppId is { } stored)
        {
            return new Ids(stored, false, gog);
        }

        var detected = SteamAppIdDetector.Detect(game, install, _services.Platform.PathComparison);
        return new Ids(detected, detected is not null, gog);
    }

    private async Task<GameArtwork> CurrentAsync(GameDefinition game)
    {
        var ids = await Task.Run(() => ResolveIds(game)).ConfigureAwait(false);
        return (await ReadAsync(game, ids, tidy: true).ConfigureAwait(false)).Art;
    }

    private sealed record Read(GameArtwork Art, LookupState State, bool HasWide, bool HasSquare);

    // What is on disk for the game. With tidy, Steam images kept for another app id and GOG images kept for another product are removed
    // first: they are another game's. A game with no id of that kind keeps them, since that is also what an install folder that cannot be
    // read looks like. Steam's images come before GOG's.
    private async Task<Read> ReadAsync(GameDefinition game, Ids ids, bool tidy)
    {
        using var lease = await _store.EnterAsync(game.Id).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var state = _store.ReadState(game.Id);
            if (tidy && ids.AppId is not null && state.AppId != ids.AppId && (_store.SteamPath(game.Id, ArtworkKind.Wide) is not null || _store.SteamPath(game.Id, ArtworkKind.Square) is not null))
            {
                _store.DeleteSteamImages(game.Id);
                state = state with { AppId = null, MissAt = null };
            }

            if (tidy && ids.GogId is not null && state.GogProductId != ids.GogId && _store.DeleteGogImages(game.Id))
            {
                state = state with { GogProductId = null };
            }

            var custom = _store.CustomPath(game.Id);
            var wide = _store.SteamPath(game.Id, ArtworkKind.Wide);
            var square = _store.SteamPath(game.Id, ArtworkKind.Square);
            var gogWide = _store.GogPath(game.Id, ArtworkKind.Wide);
            var gogSquare = _store.GogPath(game.Id, ArtworkKind.Square);
            if (custom is not null)
            {
                return new Read(new GameArtwork(game.Id, ArtworkSource.Custom, custom, custom, ids.AppId, ids.Detected), state, wide is not null, square is not null);
            }

            if (wide is not null || square is not null)
            {
                return new Read(new GameArtwork(game.Id, ArtworkSource.Steam, wide, square, ids.AppId, ids.Detected), state, wide is not null, square is not null);
            }

            return gogWide is not null || gogSquare is not null
                ? new Read(new GameArtwork(game.Id, ArtworkSource.Gog, gogWide, gogSquare, ids.AppId, ids.Detected), state, gogWide is not null, gogSquare is not null)
                : new Read(new GameArtwork(game.Id, ArtworkSource.None, null, null, ids.AppId, ids.Detected), state, false, false);
        }).ConfigureAwait(false);
    }

    private async Task<bool> IsLookupDueAsync(Read read, Ids ids)
    {
        // The game's own image makes Steam's unnecessary; a lookup waits until it is removed.
        if (read.Art.Source == ArtworkSource.Custom)
        {
            return false;
        }

        var now = _services.Time.GetUtcNow();
        var online = await IsOnlineAllowedAsync().ConfigureAwait(false);
        if (_lookedUp.TryGetValue((read.Art.GameId, ids.AppId, ids.GogId, online), out var at) && now - at < _missMemory)
        {
            return false;
        }

        if ((ids.AppId is not null || ids.GogId is not null) && read.HasWide && read.HasSquare)
        {
            return false;
        }

        return !(read.State.AppId == ids.AppId && read.State.GogProductId == ids.GogId && read.State.MissAt is { } miss && now - miss < _missMemory);
    }

    // Lookups go online only with a handler, and only while the setting is on. A setting that cannot be read cannot say it is on, so
    // it counts as off: the app makes no request that the person has not allowed.
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
            return false;
        }
    }

    // Held by a game's lookup from the read that starts it until it ends; tests take it to wait for the lookup.
    internal SemaphoreSlim LookupGateOf(string gameId) => _lookupGates.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));

    private void StartBackgroundLookup(string gameId)
    {
        var gate = LookupGateOf(gameId);
        if (!gate.Wait(0))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await LookUpAsync(gameId, force: false).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SavepointException or OperationCanceledException)
            {
                // Nothing is learned from a lookup that could not finish; the next one tries again.
                _services.Log.Warning($"The artwork lookup for game '{gameId}' could not finish.", e);
            }
            finally
            {
                gate.Release();
            }
        });
    }

    // Finds the app id, takes images from the Steam client's cache, then (for a game GOG installed, when Steam's cache has none) from GOG
    // Galaxy's cache, then from Steam's servers when allowed, and keeps what it found. Without force, only the images not already kept are
    // looked for; with it, everything is, and nothing found is thrown away.
    private async Task LookUpAsync(string gameId, bool force)
    {
        if (await _services.Catalog.GetGameAsync(gameId).ConfigureAwait(false) is not { } game)
        {
            return;
        }

        var ids = await Task.Run(() => ResolveIds(game)).ConfigureAwait(false);
        var id = ids.AppId;
        var online = await IsOnlineAllowedAsync().ConfigureAwait(false);
        var now = _services.Time.GetUtcNow();

        var found = new List<(ArtworkKind Kind, byte[] Bytes)>();
        var gogFound = new List<(ArtworkKind Kind, byte[] Bytes)>();
        var failures = new List<string>();
        var sawNotFound = false;
        var before = _store.ReadState(gameId);
        var pending = new List<ArtworkKind>();
        if (id is { } appId)
        {
            var steamFolder = _steam.FindSteamFolder();
            foreach (var kind in new[] { ArtworkKind.Wide, ArtworkKind.Square })
            {
                if (!force && before.AppId == appId && _store.SteamPath(gameId, kind) is not null)
                {
                    continue;
                }

                pending.Add(kind);
                if (steamFolder is not null && await Task.Run(() => FromSteamCache(steamFolder, appId, kind)).ConfigureAwait(false) is { } cached)
                {
                    found.Add((kind, cached));
                }
            }
        }

        // Steam's images, kept or found, are the game's art; Galaxy's are a fallback for a game with none.
        var steamHas = found.Count > 0 || ((id is null || before.AppId == id) && (_store.SteamPath(gameId, ArtworkKind.Wide) is not null || _store.SteamPath(gameId, ArtworkKind.Square) is not null));
        if (!steamHas && ids.GogId is { } product && _gog.FindWebcacheFolder() is { } webcache)
        {
            foreach (var kind in new[] { ArtworkKind.Wide, ArtworkKind.Square })
            {
                if (await Task.Run(() => FromGalaxyCache(webcache, product, kind)).ConfigureAwait(false) is { } cached)
                {
                    gogFound.Add((kind, cached));
                }
            }
        }

        // Nothing is asked of Steam's servers for a game whose art was already found on this computer.
        if (id is { } onlineApp && online && gogFound.Count == 0)
        {
            // One store answer serves both images of this lookup.
            var storeMemo = new StoreAssetsMemo();
            foreach (var kind in pending.Where(kind => found.All(f => f.Kind != kind)))
            {
                var fetched = await _cdn!.FetchAsync(onlineApp, kind, CancellationToken.None, storeMemo).ConfigureAwait(false);
                sawNotFound |= fetched.Outcome == CdnOutcome.NotFound;
                if (fetched.Outcome == CdnOutcome.Failed)
                {
                    failures.Add($"the {kind.ToString().ToLowerInvariant()} image ({fetched.Reason})");
                }

                if (fetched.Bytes is not null)
                {
                    found.Add((kind, fetched.Bytes));
                }
            }
        }

        var changed = await CommitAsync(game, ids, now, found, gogFound, sawNotFound).ConfigureAwait(false);
        if (failures.Count == 0)
        {
            _lookedUp[(gameId, id, ids.GogId, online)] = now;
            _loggedFailures.TryRemove(gameId, out _);
        }
        else
        {
            // A failure is retried at every read, so the same one is written once, not once per read.
            var line = $"The artwork lookup for game '{gameId}' (Steam app {id}) could not finish: {string.Join("; ", failures)}.";
            if (!_loggedFailures.TryGetValue(gameId, out var last) || last != line)
            {
                _loggedFailures[gameId] = line;
                _services.Log.Warning(line);
            }
        }

        if (changed)
        {
            Raise(gameId);
        }
    }

    // Nothing bigger than a user's own image is copied out of Steam's folder, whatever it is called.
    private static byte[]? FromSteamCache(string steamFolder, int appId, ArtworkKind kind) =>
        ReadSmallFile(SteamLocalCache.Find(steamFolder, appId, kind));

    // The same limit holds for Galaxy's folder.
    private static byte[]? FromGalaxyCache(string webcacheFolder, string productId, ArtworkKind kind) =>
        ReadSmallFile(GogLocalCache.Find(webcacheFolder, productId, kind));

    private static byte[]? ReadSmallFile(string? path)
    {
        if (path is null)
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

    // Writes the result under the game's lease, and only if the game still exists and still has the ids the lookup was for.
    private async Task<bool> CommitAsync(GameDefinition game, Ids ids, DateTimeOffset now, List<(ArtworkKind Kind, byte[] Bytes)> found, List<(ArtworkKind Kind, byte[] Bytes)> gogFound, bool sawNotFound)
    {
        var id = ids.AppId;
        using var lease = await _store.EnterAsync(game.Id).ConfigureAwait(false);
        if (await _services.Catalog.GetGameAsync(game.Id).ConfigureAwait(false) is not { } current
            || (await Task.Run(() => ResolveIds(current)).ConfigureAwait(false)) != ids)
        {
            return false;
        }

        return await Task.Run(() =>
        {
            var changed = false;
            var state = _store.ReadState(game.Id);
            if (id is not null && state.AppId != id && _store.DeleteSteamImages(game.Id))
            {
                changed = true;
                state = state with { AppId = null, MissAt = null };
            }

            // Images kept for another GOG product are another game's.
            if (ids.GogId is not null && state.GogProductId != ids.GogId && _store.DeleteGogImages(game.Id))
            {
                changed = true;
            }

            foreach (var (kind, bytes) in found)
            {
                // An image the store refuses is left out, as if the lookup had not found it; the next lookup looks again.
                changed |= _store.SaveSteamImage(game.Id, kind, bytes);
            }

            // Once Steam has an image of its own, Galaxy's are not kept beside it.
            var steamHas = _store.SteamPath(game.Id, ArtworkKind.Wide) is not null || _store.SteamPath(game.Id, ArtworkKind.Square) is not null;
            if (steamHas)
            {
                changed |= _store.DeleteGogImages(game.Id);
            }
            else
            {
                foreach (var (kind, bytes) in gogFound)
                {
                    changed |= _store.SaveGogImage(game.Id, kind, bytes);
                }
            }

            var complete = id is not null
                && _store.SteamPath(game.Id, ArtworkKind.Wide) is not null
                && _store.SteamPath(game.Id, ArtworkKind.Square) is not null;
            var gogNext = ids.GogId ?? state.GogProductId;

            // A lookup that found no app id learned nothing about the one the stored state is for (an install drive that is unplugged
            // names none), so that state stays: a miss recorded under it would hold back the id when it returns. A game GOG installed has a
            // product id to look by, and Galaxy's cache is only a folder read away, so a miss there is not remembered across runs.
            var next = id is null && state.AppId is not null
                ? state with { GogProductId = gogNext }
                : new LookupState(
                    id ?? state.AppId,
                    id is null ? (ids.GogId is null ? now : null) : sawNotFound ? now : complete || state.AppId != id ? null : state.MissAt,
                    gogNext);
            if (next != _store.ReadState(game.Id))
            {
                _store.WriteState(game.Id, next);
            }

            return changed;
        }).ConfigureAwait(false);
    }

    private void Raise(string gameId) => ArtworkChanged?.Invoke(this, new ArtworkChange(gameId));
}
