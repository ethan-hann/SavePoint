using Avalonia.Controls;
using Avalonia.Platform;

namespace Savepoint.App.Platform;

/// <summary>The icon every SavePoint window carries.</summary>
internal static class AppIcon
{
    /// <summary>
    /// Where the icon lives in the app's resources. Windows gets the multi-size .ico so it picks the frame that fits the
    /// title bar and the taskbar at the current scaling, instead of shrinking one large image.
    /// </summary>
    /// <param name="windows">Whether the icon is for Windows.</param>
    public static Uri ResourceUri(bool windows) =>
        new(windows ? "avares://SavePoint/Assets/Icon/savepoint.ico" : "avares://SavePoint/Assets/Icon/savepoint-256.png");

    /// <summary>Opens the icon for the platform the app runs on.</summary>
    public static WindowIcon Load()
    {
        using var stream = AssetLoader.Open(ResourceUri(OperatingSystem.IsWindows()));
        return new WindowIcon(stream);
    }
}
