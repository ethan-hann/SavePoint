using System.Globalization;

namespace Savepoint.Core.Artwork;

// Finds artwork the Steam client already downloaded, so no network is needed for it (D30).
internal static class SteamLocalCache
{
    // Steam keeps a few dozen files per app at most; the walk stops well before anything strange.
    private const int _maxDepth = 4;

    // The newest complete image for the app in Steam's library cache, or null: the per-app folder and its subfolders first, then the older flat names.
    public static string? Find(string steamFolder, int appId, ArtworkKind kind)
    {
        ArgumentNullException.ThrowIfNull(steamFolder);

        try
        {
            var cache = Path.Combine(steamFolder, "appcache", "librarycache");
            var id = appId.ToString(CultureInfo.InvariantCulture);
            var fileName = kind.SteamFileName();
            return NewestImage(Path.Combine(cache, id), fileName)
                ?? NewestImage(cache, $"{id}_{fileName}", recurse: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    // Candidates are tried newest first, so a half-written or replaced file never hides an older complete one.
    private static string? NewestImage(string folder, string fileName, bool recurse = true)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recurse,
            MaxRecursionDepth = _maxDepth,
            MatchCasing = MatchCasing.PlatformDefault,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        };
        return new DirectoryInfo(folder)
            .EnumerateFiles(fileName, options)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault(path => ImageProbe.ReadFile(path) is not null);
    }
}
