using Savepoint.Core.Artwork;
using Savepoint.Core.Catalog;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

/// <summary>Tells which games need the Steam client to be running when they are started from their program.</summary>
internal static class SteamGames
{
    /// <summary>
    /// True for a Steam game that the launch starts from its program: its install folder sits in a Steam library
    /// (<c>steamapps/common/&lt;game&gt;</c>, which is where its manifest is kept too). A Steam app id alone does not count, because a
    /// copy from GOG or a retail disc carries the app id of the Steam game it matched, or a stray <c>steam_appid.txt</c>, and starts
    /// without the client. Such a Steam game quits at once when the client is closed. A game started from a link is Steam's own to
    /// start, and a Windows program on an OS that cannot run one is refused later with its own message, so neither counts.
    /// </summary>
    /// <param name="game">The game's definition.</param>
    /// <param name="installPath">The install folder as an absolute path, or null when it cannot be resolved.</param>
    /// <param name="os">The OS the game starts on.</param>
    /// <param name="pathComparison">How the OS compares paths.</param>
    public static bool NeedsClient(GameDefinition game, string? installPath, OsKind os, StringComparison pathComparison)
    {
        ArgumentNullException.ThrowIfNull(game);
        var launch = game.Launch;
        if (launch.Method != LaunchMethod.Exe)
        {
            return false;
        }

        if (os != OsKind.Windows && launch.ExePath?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
        {
            return false;
        }

        return SteamAppIdDetector.IsInSteamLibrary(installPath, pathComparison);
    }
}
