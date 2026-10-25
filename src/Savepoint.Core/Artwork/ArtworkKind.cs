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

    // The end of the file name GOG Galaxy caches each image under: the poster for the tall image, the darkened background for the wide one.
    public static string GogCacheSuffix(this ArtworkKind kind) => kind == ArtworkKind.Wide ? "_glx_bg_top_padding_7.webp" : "_glx_vertical_cover.webp";

    // Every name the Steam client may have cached the image under: newer games use the library_ names. Hero, logo, blur and icon files are not listed on purpose.
    public static IReadOnlyList<string> SteamCacheFileNames(this ArtworkKind kind) =>
        kind == ArtworkKind.Wide
            ? ["header.jpg", "library_header.jpg"]
            : ["library_600x900.jpg", "library_capsule_2x.jpg", "library_capsule.jpg"];
}
