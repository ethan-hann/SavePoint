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
}
