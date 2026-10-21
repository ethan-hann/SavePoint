namespace Savepoint.App.Features.Common.Art;

/// <summary>The provider of an app with no artwork Api: every game keeps its name tint and nothing is ever read.</summary>
public sealed class NoArtworkProvider : IArtworkProvider
{
    private readonly ArtImages _images = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, GameArt> _arts = new(StringComparer.Ordinal);

    /// <summary>The one instance.</summary>
    public static NoArtworkProvider Instance { get; } = new();

    /// <inheritdoc />
    public GameArt For(string gameId)
    {
        lock (_gate)
        {
            if (!_arts.TryGetValue(gameId, out var art))
            {
                art = new GameArt(gameId, _images);
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
    }
}
