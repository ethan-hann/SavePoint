namespace Savepoint.Core.Tests.Platform;

/// <summary>Steam's <c>libraryfolders.vdf</c> as the client writes it, shortened to a few apps per library.</summary>
internal static class SteamFixtures
{
    // Copied from a Windows install with four libraries; the last one (N:) is on a drive that is not connected, which Steam
    // marks with a last-verified time of 0.
    public static string WindowsFile { get; } = string.Join(
        '\n',
        "\"libraryfolders\"",
        "{",
        "\t\"0\"",
        "\t{",
        "\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"",
        "\t\t\"label\"\t\t\"\"",
        "\t\t\"contentid\"\t\t\"4917324856937623168\"",
        "\t\t\"totalsize\"\t\t\"0\"",
        "\t\t\"update_clean_bytes_tally\"\t\t\"2148982081\"",
        "\t\t\"time_last_update_verified\"\t\t\"1791941135\"",
        "\t\t\"apps\"",
        "\t\t{",
        "\t\t\t\"228980\"\t\t\"1275240571\"",
        "\t\t\t\"250820\"\t\t\"5820607244\"",
        "\t\t}",
        "\t}",
        "\t\"1\"",
        "\t{",
        "\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"",
        "\t\t\"label\"\t\t\"\"",
        "\t\t\"contentid\"\t\t\"5951028793945160159\"",
        "\t\t\"totalsize\"\t\t\"8001545039872\"",
        "\t\t\"update_clean_bytes_tally\"\t\t\"2148898879\"",
        "\t\t\"time_last_update_verified\"\t\t\"1791846184\"",
        "\t\t\"apps\"",
        "\t\t{",
        "\t\t\t\"240\"\t\t\"4888872627\"",
        "\t\t\t\"620\"\t\t\"12759257499\"",
        "\t\t}",
        "\t}",
        "\t\"2\"",
        "\t{",
        "\t\t\"path\"\t\t\"X:\\\\SteamLibrary\"",
        "\t\t\"label\"\t\t\"\"",
        "\t\t\"contentid\"\t\t\"5558096771488172196\"",
        "\t\t\"totalsize\"\t\t\"4000785100800\"",
        "\t\t\"update_clean_bytes_tally\"\t\t\"2147821005\"",
        "\t\t\"time_last_update_verified\"\t\t\"1791940999\"",
        "\t\t\"apps\"",
        "\t\t{",
        "\t\t\t\"241560\"\t\t\"26342244935\"",
        "\t\t}",
        "\t}",
        "\t\"3\"",
        "\t{",
        "\t\t\"path\"\t\t\"N:\\\\SteamLibrary\"",
        "\t\t\"label\"\t\t\"\"",
        "\t\t\"contentid\"\t\t\"6891638772201935776\"",
        "\t\t\"totalsize\"\t\t\"1099492753408\"",
        "\t\t\"update_clean_bytes_tally\"\t\t\"7045661190\"",
        "\t\t\"time_last_update_verified\"\t\t\"0\"",
        "\t\t\"apps\"",
        "\t\t{",
        "\t\t\t\"447020\"\t\t\"7045661190\"",
        "\t\t}",
        "\t}",
        "}",
        "");

    // The same layout with POSIX paths, as the Linux client writes it.
    public static string LinuxFile { get; } = string.Join(
        '\n',
        "\"libraryfolders\"",
        "{",
        "\t\"0\"",
        "\t{",
        "\t\t\"path\"\t\t\"/home/test/.local/share/Steam\"",
        "\t\t\"label\"\t\t\"\"",
        "\t\t\"apps\"",
        "\t\t{",
        "\t\t\t\"228980\"\t\t\"1275240571\"",
        "\t\t}",
        "\t}",
        "\t\"1\"",
        "\t{",
        "\t\t\"path\"\t\t\"/mnt/games/SteamLibrary\"",
        "\t\t\"label\"\t\t\"\"",
        "\t\t\"apps\"",
        "\t\t{",
        "\t\t}",
        "\t}",
        "}",
        "");

    // Clients before 2021 wrote each library as a bare path, after two statistics keys.
    public static string OldLayoutFile { get; } = string.Join(
        '\n',
        "\"LibraryFolders\"",
        "{",
        "\t\"TimeNextStatsReport\"\t\t\"1600000000\"",
        "\t\"ContentStatsID\"\t\t\"-123456789\"",
        "\t\"1\"\t\t\"F:\\\\SteamLibrary\"",
        "\t\"0\"\t\t\"E:\\\\Games\\\\Steam\"",
        "}",
        "");

    /// <summary>A file listing <paramref name="libraries"/> in the current layout, with backslashes doubled as Steam writes them.</summary>
    public static string FileFor(params string[] libraries) => File(string.Join(
        '\n',
        libraries.Select((path, index) => $"\t\"{index}\"\n\t{{\n\t\t\"path\"\t\t\"{path.Replace("\\", "\\\\")}\"\n\t\t\"label\"\t\t\"\"\n\t}}")));

    /// <summary>Wraps raw entries in the file's outer block.</summary>
    public static string File(string entries) => "\"libraryfolders\"\n{\n" + entries + "\n}\n";
}
