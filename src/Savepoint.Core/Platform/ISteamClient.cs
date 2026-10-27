namespace Savepoint.Core.Platform;

/// <summary>
/// The Steam client on this machine. Games that call Steam's own library quit when the client is not running, so SavePoint asks
/// before it changes a game's folders, and starts only the client when the player agrees; it never starts a game through Steam,
/// because Steam installs a pending update before it runs a game and would overwrite the version SavePoint switched to.
/// </summary>
public interface ISteamClient
{
    /// <summary>
    /// True when the Steam client is running and a user is signed in. On Windows both come from Steam's registry key and the process
    /// list; elsewhere only the process list can say, so a running client counts as signed in. Never throws.
    /// </summary>
    bool IsReady();

    /// <summary>
    /// Starts the Steam client alone, in the background where the OS allows: no game and no app id are passed to it. Returns as
    /// soon as the start was handed to the OS, so callers poll <see cref="IsReady"/> for the sign-in. Never throws.
    /// </summary>
    /// <returns>False when Steam could not be found or the OS refused to start it.</returns>
    bool TryStart();
}
