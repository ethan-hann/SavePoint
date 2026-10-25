namespace Savepoint.Core.Artwork;

// Finds artwork GOG Galaxy already downloaded, so no network is needed for it. Galaxy keeps it at
// <webcache>/<account id>/gog/<product id>/<hash>_glx_<name>.webp, one folder per signed-in account; the tall poster is "vertical_cover"
// and the wide picture is "bg_top_padding_7". The same folder holds a 112 pixel icon, which is neither.
internal static class GogLocalCache
{
    // The newest complete image for the product in any account's folder, or null. The product id becomes a folder name, so it must be digits.
    public static string? Find(string webcacheFolder, string productId, ArtworkKind kind)
    {
        ArgumentNullException.ThrowIfNull(webcacheFolder);
        ArgumentNullException.ThrowIfNull(productId);

        if (productId.Length is 0 or > 20 || !productId.All(char.IsAsciiDigit) || !IsPlainFolder(new DirectoryInfo(webcacheFolder)))
        {
            return null;
        }

        try
        {
            var options = new EnumerationOptions
            {
                MatchCasing = MatchCasing.CaseInsensitive,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            };
            var pattern = "*" + kind.GogCacheSuffix();
            return new DirectoryInfo(webcacheFolder)
                .EnumerateDirectories("*", options)
                .Where(account => account.Name.All(char.IsAsciiDigit))
                .Select(account => new DirectoryInfo(Path.Combine(account.FullName, "gog")))
                .Where(IsPlainFolder)
                .Select(gog => new DirectoryInfo(Path.Combine(gog.FullName, productId)))
                .Where(IsPlainFolder)
                .SelectMany(folder => folder.EnumerateFiles(pattern, options))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault(path => ImageProbe.ReadFile(path) is not null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    // Links are never followed: a link here could lead anywhere on the disk.
    private static bool IsPlainFolder(DirectoryInfo folder) => folder.Exists && !folder.Attributes.HasFlag(FileAttributes.ReparsePoint);
}
