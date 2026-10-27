using System.Runtime.Versioning;
using System.Security;

namespace Savepoint.Core.Platform.Windows;

/// <summary>Reads where the Steam client says it is installed.</summary>
internal static class WindowsSteamRegistry
{
    /// <summary>The <c>SteamPath</c> value as stored (forward slashes, installer's case); null when it is missing or unreadable.</summary>
    [SupportedOSPlatform("windows")]
    public static string? ReadSteamPath()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath") as string;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// The running client's process id and signed-in account (<c>ActiveProcess\pid</c> and <c>ActiveUser</c>). Null when the key or
    /// either value is missing or unreadable; the account is 0 while Steam is closed or nobody has signed in.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static SteamSignIn? ReadSignIn()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            return key?.GetValue("pid") is int pid && key.GetValue("ActiveUser") is int user
                ? new SteamSignIn(pid, unchecked((uint)user))
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// The account id of the signed-in Steam user (<c>ActiveProcess\ActiveUser</c>), which is the name of its <c>userdata</c> folder.
    /// Null when the value is missing, unreadable or 0, which is what it holds while Steam is closed.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static uint? ReadActiveUser()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            return key?.GetValue("ActiveUser") is int value and not 0 ? unchecked((uint)value) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }
}
