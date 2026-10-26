using System.Globalization;
using Savepoint.Core.Artwork;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

/// <summary>What SavePoint knows about a game that Steam starts for a link.</summary>
internal static class SteamLaunch
{
    private const string _marker = "SteamLaunch AppId=";

    /// <summary>The app id a <c>steam://run/&lt;id&gt;</c> or <c>steam://rungameid/&lt;id&gt;</c> link names.</summary>
    /// <param name="uri">The link as the game's launch settings hold it.</param>
    /// <returns>The id, or null for any other link.</returns>
    public static int? AppIdOf(string? uri) => SteamAppIdDetector.AppIdOfLink(uri);

    /// <summary>
    /// True for the process Steam on Linux keeps alive for a game from the moment it begins starting it until the game has ended: its
    /// <c>reaper</c> helper, whose command line reads <c>SteamLaunch AppId=&lt;id&gt;</c>. Steam's first start of a game with a new
    /// Proton prefix runs two of them one after the other, so the helper can be missing for a moment between them.
    /// </summary>
    /// <param name="process">A process from the list; only one with a command line can match.</param>
    /// <param name="appId">The app id of the game being started.</param>
    public static bool IsLaunchHelper(RunningProcess process, int appId)
    {
        if (process.CommandLine is not { } line || !string.Equals(process.Name, "reaper", StringComparison.Ordinal))
        {
            return false;
        }

        // The id must end at a space or the end of the line, so app 240 is not taken for app 2400.
        var wanted = _marker + appId.ToString(CultureInfo.InvariantCulture);
        for (var at = line.IndexOf(wanted, StringComparison.Ordinal); at >= 0; at = line.IndexOf(wanted, at + 1, StringComparison.Ordinal))
        {
            var end = at + wanted.Length;
            if (end == line.Length || line[end] == ' ')
            {
                return true;
            }
        }

        return false;
    }
}
