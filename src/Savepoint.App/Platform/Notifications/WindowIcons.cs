using System.Runtime.InteropServices;

namespace Savepoint.App.Platform.Notifications;

/// <summary>Finds the icon a window shows, to put in the notification area beside its notifications.</summary>
internal static class WindowIcons
{
    private const uint _getIconMessage = 0x007F;
    private const uint _abortIfHung = 0x0002;
    private const uint _timeoutMilliseconds = 250;
    private const int _applicationIcon = 32512;

    // Small icon first, since the notification area draws it at 16 pixels.
    private static readonly nint[] _iconKinds = [2, 0, 1];

    /// <summary>
    /// The window's own icon, scaled by the window if need be, or the stock application icon when it has none. The icons
    /// belong to the window or the system, so nothing here needs to be released. Asks the window with a timeout, because
    /// its thread may be busy and a notification must not wait on it.
    /// </summary>
    public static nint For(nint window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        foreach (var kind in _iconKinds)
        {
            var reply = SendMessageTimeoutW(window, _getIconMessage, kind, 0, _abortIfHung, _timeoutMilliseconds, out var icon);
            if (reply != 0 && icon != 0)
            {
                return icon;
            }
        }

        return LoadIconW(0, _applicationIcon);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint SendMessageTimeoutW(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint LoadIconW(nint instance, nint iconName);
}
