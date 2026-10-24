using System.Text;
using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Platform;

namespace Savepoint.Core.Tests.Library;

// Store libraries on disk built from what real Steam and GOG Galaxy installs write. The manifests copy the layout and the values
// of the 177 manifests on one PC (2026-10-24): StateFlags 4 is installed, 518 and 22 are installed and waiting for or running an
// update, and the tools (Steamworks Shared, SteamVR) have manifests like any game. The account id is replaced and no game file
// is copied: a game folder holds one small file.
internal static class LibraryFixtures
{
    // The only Steam manifests in the fixtures that did not come from the PC: Steam ships these tools on Linux.
    public const string ProtonName = "Proton 9.0 (Beta)";
    public const string LinuxRuntimeName = "Steam Linux Runtime 3.0 (sniper)";

    public static PathSyntax Syntax { get; } = PathSyntax.For(PlatformFactory.CurrentOs);

    // The whole manifest of Counter-Strike: Source as the client wrote it, with only the account id replaced.
    public static string CounterStrikeSourceManifest { get; } = string.Join(
        '\n',
        "\"AppState\"",
        "{",
        "\t\"appid\"\t\t\"240\"",
        "\t\"Universe\"\t\t\"1\"",
        "\t\"LauncherPath\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\\\\steam.exe\"",
        "\t\"name\"\t\t\"Counter-Strike: Source\"",
        "\t\"StateFlags\"\t\t\"4\"",
        "\t\"installdir\"\t\t\"Counter-Strike Source\"",
        "\t\"LastUpdated\"\t\t\"1789691940\"",
        "\t\"LastPlayed\"\t\t\"1740941939\"",
        "\t\"SizeOnDisk\"\t\t\"4888872627\"",
        "\t\"StagingSize\"\t\t\"0\"",
        "\t\"buildid\"\t\t\"25338318\"",
        "\t\"LastOwner\"\t\t\"76561190000000000\"",
        "\t\"DownloadType\"\t\t\"1\"",
        "\t\"UpdateResult\"\t\t\"0\"",
        "\t\"BytesToDownload\"\t\t\"447568\"",
        "\t\"BytesDownloaded\"\t\t\"447568\"",
        "\t\"BytesToStage\"\t\t\"295034887\"",
        "\t\"BytesStaged\"\t\t\"295034887\"",
        "\t\"TargetBuildID\"\t\t\"25338318\"",
        "\t\"AutoUpdateBehavior\"\t\t\"0\"",
        "\t\"AllowOtherDownloadsWhileRunning\"\t\t\"0\"",
        "\t\"ScheduledAutoUpdate\"\t\t\"0\"",
        "\t\"InstalledDepots\"",
        "\t{",
        "\t\t\"241\"",
        "\t\t{",
        "\t\t\t\"manifest\"\t\t\"6505320509489453551\"",
        "\t\t\t\"size\"\t\t\"4566652976\"",
        "\t\t}",
        "\t\t\"240\"",
        "\t\t{",
        "\t\t\t\"manifest\"\t\t\"8533645887610833256\"",
        "\t\t\t\"size\"\t\t\"825745\"",
        "\t\t}",
        "\t\t\"232331\"",
        "\t\t{",
        "\t\t\t\"manifest\"\t\t\"8754485147351195844\"",
        "\t\t\t\"size\"\t\t\"321393906\"",
        "\t\t}",
        "\t}",
        "\t\"UserConfig\"",
        "\t{",
        "\t\t\"language\"\t\t\"english\"",
        "\t}",
        "\t\"MountedConfig\"",
        "\t{",
        "\t\t\"language\"\t\t\"english\"",
        "\t}",
        "}",
        "");

    // A shorter manifest laid out as Steam writes it (tab-separated, LF lines, backslashes doubled) for any game.
    public static string Manifest(int appId, string name, string installDir, int stateFlags) => string.Join(
        '\n',
        "\"AppState\"",
        "{",
        $"\t\"appid\"\t\t\"{appId}\"",
        "\t\"Universe\"\t\t\"1\"",
        "\t\"LauncherPath\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\\\\steam.exe\"",
        $"\t\"name\"\t\t\"{name}\"",
        $"\t\"StateFlags\"\t\t\"{stateFlags}\"",
        $"\t\"installdir\"\t\t\"{installDir}\"",
        "\t\"LastUpdated\"\t\t\"1788571895\"",
        "\t\"LastPlayed\"\t\t\"0\"",
        "\t\"SizeOnDisk\"\t\t\"16092533099\"",
        "\t\"StagingSize\"\t\t\"0\"",
        "\t\"buildid\"\t\t\"24914197\"",
        "\t\"LastOwner\"\t\t\"76561190000000000\"",
        "\t\"DownloadType\"\t\t\"1\"",
        "\t\"UpdateResult\"\t\t\"0\"",
        "\t\"AutoUpdateBehavior\"\t\t\"0\"",
        "\t\"InstalledDepots\"",
        "\t{",
        $"\t\t\"{appId + 1}\"",
        "\t\t{",
        "\t\t\t\"manifest\"\t\t\"4940892828028256588\"",
        "\t\t\t\"size\"\t\t\"7490529657\"",
        "\t\t}",
        "\t}",
        "\t\"UserConfig\"",
        "\t{",
        "\t\t\"language\"\t\t\"english\"",
        "\t}",
        "}",
        "");
}

// A Steam client folder in a temp folder with libraries next to it.
internal sealed class SteamLibraryRig : IDisposable
{
    private readonly List<string> _libraries = [];

    public SteamLibraryRig()
    {
        Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "savepoint-steamlib-" + Guid.NewGuid().ToString("N")));
        SteamFolder = Path.Combine(Root, "Steam");
        Directory.CreateDirectory(Path.Combine(SteamFolder, "steamapps", "common"));
        _libraries.Add(SteamFolder);
    }

    public string Root { get; }

    public string SteamFolder { get; }

    // Makes another library folder, listed in libraryfolders.vdf unless it is told not to be.
    public string AddLibrary(string name = "SteamLibrary", bool create = true)
    {
        var folder = Path.Combine(Root, name);
        if (create)
        {
            Directory.CreateDirectory(Path.Combine(folder, "steamapps", "common"));
        }

        _libraries.Add(folder);
        return folder;
    }

    // Writes a manifest into a library and, when asked, the game's folder with one file in it. Returns the game folder.
    public string AddGame(string library, int appId, string name, string installDir, int stateFlags = 4, bool installed = true)
    {
        var steamApps = Path.Combine(library, "steamapps");
        File.WriteAllText(Path.Combine(steamApps, $"appmanifest_{appId}.acf"), LibraryFixtures.Manifest(appId, name, installDir, stateFlags), new UTF8Encoding(false));
        var folder = Path.Combine(steamApps, "common", installDir);
        if (installed)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "game.bin"), "x");
        }

        return folder;
    }

    // Writes libraryfolders.vdf the way the client does, listing every library made so far.
    public void WriteLibraryList()
    {
        File.WriteAllText(Path.Combine(SteamFolder, "steamapps", "libraryfolders.vdf"), SteamFixtures.FileFor([.. _libraries]), new UTF8Encoding(false));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp folder left behind is harmless.
        }
    }
}
