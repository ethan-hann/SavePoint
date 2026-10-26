using System.Globalization;
using System.Text.RegularExpressions;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Artwork;

// Works out a game's Steam app id from what is on disk. It only reads files and never changes the game.
internal static partial class SteamAppIdDetector
{
    // A text file holding one number is a few bytes; anything bigger is something else.
    private const int _appIdFileLimit = 64;

    // The real files sit up to three folders down (Online/Binaries/Win32); 200 folders is far more than a game needs and keeps the search cheap.
    private const int _subfolderDepth = 3;
    private const int _subfolderBudget = 200;
    private const int _folderListLimit = 1000;

    // Tries the launch link, then the Steam library's app manifests, then steam_appid.txt in the install folder or just below it;
    // null when none agrees. The manifest comes first because games ship a steam_appid.txt that names a different app than the store lists.
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

        return FromManifest(installPath, pathComparison) ?? FromAppIdFile(installPath);
    }

    [GeneratedRegex(@"^steam://(?:run|rungameid)/(\d+)(?:[/?#].*)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LaunchLink();

    [GeneratedRegex(@"^appmanifest_(\d+)\.acf\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ManifestFileName();

    // Valve's key-value text: the line that reads  "installdir"  "Folder Name".
    [GeneratedRegex("^\\s*\"installdir\"\\s+\"(?<dir>[^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstallDirLine();

    // Only a game that starts through a link has a launch link; a stale one left on a program game is ignored.
    private static int? FromLaunchLink(LaunchConfig launch) => launch.Method == LaunchMethod.Uri ? AppIdOfLink(launch.Uri) : null;

    // The app id a steam://run/ or steam://rungameid/ link names; null for any other text.
    public static int? AppIdOfLink(string? uri) =>
        uri is not null && LaunchLink().Match(uri.Trim()) is { Success: true } match ? ParseId(match.Groups[1].Value) : null;

    // The install folder's own file first, then a bounded look through its subfolders (many games keep it beside the executable).
    private static int? FromAppIdFile(string installPath) => ReadAppIdFile(installPath) ?? FromSubfolders(installPath);

    private static int? ReadAppIdFile(string folder)
    {
        try
        {
            var file = new FileInfo(Path.Combine(folder, "steam_appid.txt"));
            if (!file.Exists || file.Length > _appIdFileLimit || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return null;
            }

            return ParseId(TrimWhitespaceAndNul(File.ReadAllText(file.FullName)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    // Breadth first, so a shallower file wins; links are never entered, and the number of folders looked at is capped.
    private static int? FromSubfolders(string installPath)
    {
        var budget = _subfolderBudget;
        var level = new List<string> { installPath };
        for (var depth = 1; depth <= _subfolderDepth && level.Count > 0; depth++)
        {
            var next = new List<string>();
            foreach (var parent in level)
            {
                foreach (var child in ChildFolders(parent).Take(budget))
                {
                    budget--;
                    if (ReadAppIdFile(child) is { } id)
                    {
                        return id;
                    }

                    next.Add(child);
                }

                if (budget <= 0)
                {
                    return null;
                }
            }

            level = next;
        }

        return null;
    }

    // Sorted so the result does not depend on the order the file system lists folders in.
    private static List<string> ChildFolders(string parent)
    {
        try
        {
            var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System, IgnoreInaccessible = true };
            var names = Directory.EnumerateDirectories(parent, "*", options).Take(_folderListLimit).ToList();
            names.Sort(StringComparer.Ordinal);
            return names;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    // Steam writes the id followed by a newline and a NUL byte; neither is part of the number.
    private static string TrimWhitespaceAndNul(string text) =>
        text.AsSpan().Trim(['\0', ' ', '\t', '\r', '\n', '\v', '\f', ' ', '﻿']).ToString();

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
