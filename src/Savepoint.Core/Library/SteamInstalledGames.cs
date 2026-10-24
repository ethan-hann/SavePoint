using System.Globalization;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Paths;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Library;

// Lists the games Steam has installed, from its library list and each app manifest. Only reads files; never throws for a bad file.
internal static class SteamInstalledGames
{
    private const int _maxManifestsPerLibrary = 5000;
    private const string _manifestPattern = "appmanifest_*.acf";

    // Steam's own helper packages, which have manifests like any game: the shared redistributables and SteamVR (both seen in real
    // libraries). Other tools are told apart by name below, since their ids change with each release.
    private static readonly HashSet<int> _toolIds = [228980, 250820];

    private static readonly string[] _toolNamePrefixes = ["Proton ", "Steam Linux Runtime", "Steamworks Common Redistributables"];

    /// <summary>
    /// Every installed game in the Steam folder's own library and the libraries it lists, tools left out, each game once (the first
    /// library holding it wins), sorted by name. A library that is not there, such as one on a drive that is not connected, is
    /// skipped. A game counts as installed when its folder under <c>steamapps/common</c> exists, whatever the manifest's state says,
    /// because a game that is updating has a state of its own and is still on disk.
    /// </summary>
    /// <param name="steamFolder">The Steam client's folder.</param>
    /// <param name="syntax">How paths in <c>libraryfolders.vdf</c> are written on this computer.</param>
    public static IReadOnlyList<InstalledGame> Scan(string steamFolder, PathSyntax syntax)
    {
        var found = new Dictionary<int, InstalledGame>();
        foreach (var library in Libraries(steamFolder, syntax))
        {
            ScanLibrary(library, found);
        }

        return [.. found.Values.OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase).ThenBy(game => game.StoreId, StringComparer.Ordinal)];
    }

    private static List<string> Libraries(string steamFolder, PathSyntax syntax)
    {
        var libraries = new List<string> { steamFolder };
        var text = ReadSmallFile(Path.Combine(steamFolder, "steamapps", "libraryfolders.vdf"));
        foreach (var listed in SteamLibraryFile.LibraryPaths(text, syntax))
        {
            if (!libraries.Contains(listed, StringComparer.FromComparison(syntax.Comparison)))
            {
                libraries.Add(listed);
            }
        }

        return libraries;
    }

    private static void ScanLibrary(string library, Dictionary<int, InstalledGame> found)
    {
        var steamApps = Path.Combine(library, "steamapps");
        List<string> manifests;
        try
        {
            if (!Directory.Exists(steamApps))
            {
                return;
            }

            manifests = [.. Directory.EnumerateFiles(steamApps, _manifestPattern).Take(_maxManifestsPerLibrary).Order(StringComparer.Ordinal)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var manifest in manifests)
        {
            if (ReadGame(manifest, steamApps) is { SteamAppId: { } appId } game)
            {
                found.TryAdd(appId, game);
            }
        }
    }

    private static InstalledGame? ReadGame(string manifestFile, string steamApps)
    {
        var state = ValveKeyValues.ParseRoot(ReadSmallFile(manifestFile), "AppState");
        if (state is null
            || !int.TryParse(state.TextOf("appid"), NumberStyles.None, CultureInfo.InvariantCulture, out var appId)
            || appId <= 0
            || state.TextOf("installdir") is not { } installDir
            || !IsPlainFolderName(installDir))
        {
            return null;
        }

        var name = state.TextOf("name") is { Length: > 0 } named && !string.IsNullOrWhiteSpace(named) ? named.Trim() : installDir;
        if (IsTool(appId, name, installDir))
        {
            return null;
        }

        var folder = Path.Combine(steamApps, "common", installDir);
        if (!Directory.Exists(folder))
        {
            return null;
        }

        var launch = new LaunchConfig(LaunchMethod.Uri, Uri: $"steam://rungameid/{appId.ToString(CultureInfo.InvariantCulture)}");
        return new InstalledGame(LibrarySource.Steam, appId.ToString(CultureInfo.InvariantCulture), name, folder, installDir, launch, appId);
    }

    private static bool IsTool(int appId, string name, string installDir) =>
        _toolIds.Contains(appId)
        || _toolNamePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        || installDir.StartsWith("SteamLinuxRuntime", StringComparison.OrdinalIgnoreCase);

    // A manifest is data from disk: its folder name must stay inside steamapps/common, so no separator, drive, or dot segment.
    private static bool IsPlainFolderName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name is not ("." or "..")
        && name.IndexOfAny(['/', '\\', ':', '\0']) < 0;

    // Manifests and the library list are a few KB; a file past the reader's bound is not one.
    private static string? ReadSmallFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length <= ValveKeyValues.MaxChars ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
