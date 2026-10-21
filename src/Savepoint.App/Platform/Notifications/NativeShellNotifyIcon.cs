using System.Runtime.InteropServices;

namespace Savepoint.App.Platform.Notifications;

/// <summary>Talks to the real Windows notification area through <c>Shell_NotifyIconW</c>.</summary>
internal sealed class NativeShellNotifyIcon : IShellNotifyIcon
{
    /// <inheritdoc />
    public bool Send(NotifyIconRequest request)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var data = NotifyIconData.Create(request);
        return Shell_NotifyIconW((uint)request.Command, ref data);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
}
