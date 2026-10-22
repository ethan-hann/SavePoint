using Savepoint.Core.Api;

namespace Savepoint.App.Features.Common.Art;

/// <summary>
/// The real <see cref="IArtworkProvider"/>, over the artwork Api. A game's artwork is read the first time a tile showing it is on
/// screen, once the UI has nothing more urgent to do, and again whenever the Api says it changed.
/// </summary>
public sealed class ArtworkProvider : IArtworkProvider, IDisposable
{
    private readonly IArtworkApi _api;
    private readonly IUiDispatcher _dispatcher;
    private readonly ArtImages _images;
    private readonly object _gate = new();
    private readonly Dictionary<string, GameArt> _arts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _onScreen = new(StringComparer.Ordinal);

    // Reads of one game can finish in any order; a read that finishes after a later one was already shown is dropped.
    private readonly Dictionary<string, long> _started = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _shown = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>Creates the provider and starts listening for artwork changes.</summary>
    /// <param name="api">The artwork Api.</param>
    /// <param name="dispatcher">Brings Api events and results to the UI thread.</param>
    /// <param name="images">Decodes and caches bitmaps; a new one when null.</param>
    public ArtworkProvider(IArtworkApi api, IUiDispatcher dispatcher, ArtImages? images = null)
    {
        _api = api;
        _dispatcher = dispatcher;
        _images = images ?? new ArtImages();
        _api.ArtworkChanged += OnArtworkChanged;
    }

    /// <inheritdoc />
    public GameArt For(string gameId)
    {
        lock (_gate)
        {
            if (!_arts.TryGetValue(gameId, out var art))
            {
                art = new GameArt(gameId, _images) { OnScreen = Request };
                _arts[gameId] = art;
            }

            return art;
        }
    }

    /// <inheritdoc />
    public GameArt Preview(string gameId) => new(gameId, _images);

    /// <inheritdoc />
    public void Refresh()
    {
        List<GameArt> shown;
        lock (_gate)
        {
            shown = [.. _onScreen.Select(id => _arts[id])];
        }

        foreach (var art in shown)
        {
            _ = ReadAsync(art, contentMayHaveChanged: false);
        }
    }

    /// <summary>Stops listening for artwork changes.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        _api.ArtworkChanged -= OnArtworkChanged;
    }

    // The first tile of a game on screen starts its read, but only once the UI has nothing else waiting, so no lookup delays a frame.
    private void Request(GameArt art)
    {
        lock (_gate)
        {
            if (_disposed || !_onScreen.Add(art.GameId))
            {
                return;
            }
        }

        _dispatcher.PostWhenIdle(() => _ = ReadAsync(art, contentMayHaveChanged: false));
    }

    private void OnArtworkChanged(object? sender, ArtworkChange change) =>
        _dispatcher.Post(() =>
        {
            GameArt? art;
            lock (_gate)
            {
                art = !_disposed && _onScreen.Contains(change.GameId) ? _arts[change.GameId] : null;
            }

            if (art is not null)
            {
                // The files under the same paths may be new ones, so the tiles read them again.
                _ = ReadAsync(art, contentMayHaveChanged: true);
            }
        });

    // A read that fails changes nothing: the tile keeps what it shows and the next change reads again.
    private async Task ReadAsync(GameArt art, bool contentMayHaveChanged)
    {
        long ticket;
        lock (_gate)
        {
            ticket = _started[art.GameId] = _started.GetValueOrDefault(art.GameId) + 1;
        }

        var read = await _api.GetAsync(art.GameId).ConfigureAwait(false);
        if (!read.Ok)
        {
            return;
        }

        _dispatcher.Post(() =>
        {
            lock (_gate)
            {
                if (_disposed || ticket < _shown.GetValueOrDefault(art.GameId))
                {
                    return;
                }

                _shown[art.GameId] = ticket;
            }

            art.Show(read.Value, contentMayHaveChanged);
        });
    }
}
