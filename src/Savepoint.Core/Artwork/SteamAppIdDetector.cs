using System.Globalization;
using System.Text.RegularExpressions;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Artwork;

// Works out a game's Steam app id from what is on disk (D30). It only reads files and never changes the game.
internal static partial class SteamAppIdDetector
{
    // A text file holding one number is a few bytes; anything bigger is something else.
    private const int _appIdFileLimit = 64;

    // Tries the launch link, then steam_appid.txt in the install folder, then the Steam library's app manifests; null when none agrees.
    public static int? Detect(GameDefinition game, string? installPath, StringComparison pathComparison)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (FromLaunchLink(game.Launch) is { } linked)
        {
            return linked;
        }

        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }

        return FromAppIdFile(installPath) ?? FromManifest(installPath, pathComparison);
    }

    [GeneratedRegex(@"^steam://(?:run|rungameid)/(\d+)(?:[/?#].*)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LaunchLink();

    [GeneratedRegex(@"^appmanifest_(\d+)\.acf\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ManifestFileName();

    // Valve's key-value text: the line that reads  "installdir"  "Folder Name".
    [GeneratedRegex("^\\s*\"installdir\"\\s+\"(?<dir>[^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstallDirLine();

    // Only a game that starts through a link has a launch link; a stale one left on a program game is ignored.
    private static int? FromLaunchLink(LaunchConfig launch) =>
        launch.Method == LaunchMethod.Uri && launch.Uri is { } uri && LaunchLink().Match(uri.Trim()) is { Success: true } match
            ? ParseId(match.Groups[1].Value)
            : null;

    private static int? FromAppIdFile(string installPath)
    {
        try
        {
            var file = new FileInfo(Path.Combine(installPath, "steam_appid.txt"));
            if (!file.Exists || file.Length > _appIdFileLimit)
            {
                return null;
            }

            return ParseId(File.ReadAllText(file.FullName).Trim());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    // A folder under <library>/steamapps/common/<dir> belongs to the app whose manifest in <library>/steamapps names <dir> as its installdir.
    private static int? FromManifest(string installPath, StringComparison pathComparison)
    {
        try
        {
            var steamApps = FindSteamAppsFolder(installPath, pathComparison, out var gameFolder);
            if (steamApps is null || !Directory.Exists(steamApps.FullName))
            {
                return null;
            }

            int? lowest = null;
            foreach (var manifest in steamApps.EnumerateFiles("appmanifest_*.acf"))
            {
                if (ManifestFileName().Match(manifest.Name) is not { Success: true } named
                    || ParseId(named.Groups[1].Value) is not { } appId
                    || (lowest is { } best && appId >= best))
                {
                    continue;
                }

                if (string.Equals(InstallDirOf(manifest), gameFolder, pathComparison))
                {
                    lowest = appId;
                }
            }

            return lowest;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    // Walks up from the install folder to the first place that sits at steamapps/common/<dir>; the install folder may be <dir> or inside it.
    private static DirectoryInfo? FindSteamAppsFolder(string installPath, StringComparison pathComparison, out string gameFolder)
    {
        gameFolder = string.Empty;
        for (var folder = new DirectoryInfo(Path.TrimEndingDirectorySeparator(installPath)); folder.Parent is { } common; folder = common)
        {
            if (string.Equals(common.Name, "common", pathComparison)
                && common.Parent is { } steamApps
                && string.Equals(steamApps.Name, "steamapps", pathComparison))
            {
                gameFolder = folder.Name;
                return steamApps;
            }
        }

        return null;
    }

    private static string? InstallDirOf(FileInfo manifest)
    {
        foreach (var line in File.ReadLines(manifest.FullName))
        {
            if (InstallDirLine().Match(line) is { Success: true } match)
            {
                return match.Groups["dir"].Value;
            }
        }

        return null;
    }

    // A positive number that fits an int: Steam app ids do, and larger ids name non-Steam shortcuts.
    private static int? ParseId(string digits) =>
        digits.Length is > 0 and <= 10 && digits.All(char.IsAsciiDigit)
        && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : null;
}
