namespace Savepoint.App.Features.Common.Art;

/// <summary>Which of a game's two images a tile wants: the wide one for a card, or the tall one for a square tile (D30).</summary>
public enum ArtShape
{
    /// <summary>The wide image, drawn where the tile is clearly wider than tall.</summary>
    Wide,

    /// <summary>The square or tall image, drawn everywhere else.</summary>
    Square,
}
