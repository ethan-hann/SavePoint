namespace Savepoint.Core.Artwork;

// The two images a game can have: the wide one for the card, and the tall one for the square tiles.
internal enum ArtworkKind
{
    Wide,
    Square,
}

internal static class ArtworkKinds
{
    // The file names Steam uses for the two images, in its library cache and on its servers.
    public static string SteamFileName(this ArtworkKind kind) => kind == ArtworkKind.Wide ? "header.jpg" : "library_600x900.jpg";
}
