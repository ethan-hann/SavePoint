using System.Runtime.InteropServices;

namespace Savepoint.App.Platform;

/// <summary>The fields of the <c>_NET_ACTIVE_WINDOW</c> client message that asks the window manager to raise and focus a window.</summary>
/// <param name="Window">The X window to activate.</param>
/// <param name="MessageType">The atom name of the message.</param>
/// <param name="Format">The bit width of each data item.</param>
/// <param name="SourceIndication">1 when an application asks, 2 when a pager asks.</param>
/// <param name="Timestamp">The server time of the user action behind the request, or 0 for "now".</param>
/// <param name="CurrentActiveWindow">The window of this application that is active now, or 0.</param>
internal readonly record struct ActiveWindowMessage(
    nint Window,
    string MessageType,
    int Format,
    nint SourceIndication,
    nint Timestamp,
    nint CurrentActiveWindow);

/// <summary>
/// Raises a window the way a second launch of the app should, on a desktop that runs the app as an X11 client. Avalonia's own
/// activation stamps the request with the time of this process's last event, which GNOME treats as stale input and answers
/// with a "ready" notice instead of raising the window.
/// </summary>
internal static class X11WindowRaiser
{
    private const int _clientMessage = 33;
    private const nint _substructureNotifyMask = 1 << 19;
    private const nint _substructureRedirectMask = 1 << 20;

    /// <summary>Builds the request for a window.</summary>
    /// <param name="window">The X window id.</param>
    public static ActiveWindowMessage MessageFor(nint window) => new(window, "_NET_ACTIVE_WINDOW", 32, 1, 0, 0);

    /// <summary>Sends the request to the X server.</summary>
    /// <param name="window">The X window id.</param>
    /// <returns>True when the request was sent; false when this is not an X11 desktop or the server could not be reached.</returns>
    public static bool TryRaise(nint window)
    {
        if (window == 0 || !OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            return Send(MessageFor(window));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static bool Send(ActiveWindowMessage message)
    {
        // Its own connection: Avalonia's is private to its event loop, and a request needs no reply.
        var display = XOpenDisplay(0);
        if (display == 0)
        {
            return false;
        }

        try
        {
            var root = XDefaultRootWindow(display);
            var eventData = new XClientMessageEvent
            {
                Type = _clientMessage,
                SendEvent = 1,
                Display = display,
                Window = message.Window,
                MessageType = XInternAtom(display, message.MessageType, 0),
                Format = message.Format,
                Data0 = message.SourceIndication,
                Data1 = message.Timestamp,
                Data2 = message.CurrentActiveWindow,
            };
            var sent = XSendEvent(display, root, 0, _substructureNotifyMask | _substructureRedirectMask, ref eventData) != 0;
            XSync(display, 0);
            return sent;
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    // XEvent is a 192-byte union on 64-bit; the client message is its first 96 bytes, and XSendEvent copies the whole union.
    [StructLayout(LayoutKind.Sequential, Size = 192)]
    private struct XClientMessageEvent
    {
        public int Type;
        public nint Serial;
        public int SendEvent;
        public nint Display;
        public nint Window;
        public nint MessageType;
        public int Format;
        public nint Data0;
        public nint Data1;
        public nint Data2;
        public nint Data3;
        public nint Data4;
    }

    [DllImport("libX11.so.6")]
    private static extern nint XOpenDisplay(nint name);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(nint display);

    [DllImport("libX11.so.6")]
    private static extern nint XDefaultRootWindow(nint display);

    [DllImport("libX11.so.6", CharSet = CharSet.Ansi)]
    private static extern nint XInternAtom(nint display, string name, int onlyIfExists);

    [DllImport("libX11.so.6")]
    private static extern int XSendEvent(nint display, nint window, int propagate, nint eventMask, ref XClientMessageEvent message);

    [DllImport("libX11.so.6")]
    private static extern int XSync(nint display, int discard);
}
