namespace Savepoint.App.Platform.Notifications;

/// <summary>
/// Raises notifications as <c>Shell_NotifyIconW</c> balloons, which Windows 10 and 11 deliver as toasts under the app's
/// name and icon. A balloon belongs to a notification area icon, so the icon is added with the first notification and
/// removed a while after the last one, and always on <see cref="Dispose"/>, so none outlives the app.
/// </summary>
internal sealed class WindowsNotificationService : INotificationService, IDisposable
{
    public const uint IconId = 1;

    private readonly Func<nint> _windowHandle;
    private readonly IShellNotifyIcon _shell;
    private readonly Func<nint, nint> _iconFor;
    private readonly string _appName;
    private readonly TimeSpan _iconLifetime;
    private readonly TimeProvider _time;

    private readonly object _gate = new();
    private ITimer? _removal;
    // Tells a removal timer that fired late that a newer notification has since moved the deadline.
    private long _generation;
    // The window the tray icon is attached to; zero while there is no icon.
    private nint _iconWindow;
    private bool _disposed;

    /// <param name="windowHandle">The handle of the main window, or zero while it has none. Read on every notification.</param>
    /// <param name="shell">Talks to the notification area.</param>
    /// <param name="iconFor">Finds the icon to show for a window.</param>
    /// <param name="appName">The hover text of the notification area icon.</param>
    /// <param name="iconLifetime">How long the icon stays after the latest notification; long enough for the toast to appear.</param>
    /// <param name="time">Times the removal of the icon.</param>
    public WindowsNotificationService(
        Func<nint> windowHandle,
        IShellNotifyIcon shell,
        Func<nint, nint> iconFor,
        string appName,
        TimeSpan iconLifetime,
        TimeProvider time)
    {
        _windowHandle = windowHandle;
        _shell = shell;
        _iconFor = iconFor;
        _appName = appName;
        _iconLifetime = iconLifetime;
        _time = time;
    }

    /// <inheritdoc />
    public void Show(string title, string body)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                ShowLocked(title, body);
            }
            catch (Exception)
            {
                // The shell is reached through window messages and can fail in ways that must not reach the caller.
            }
        }
    }

    /// <summary>
    /// Removes the notification area icon now, if one is showing, and cancels its pending removal. Later notifications add
    /// it again. Call it before the window goes away: the shell drops the icon of a destroyed window only when the mouse passes over it.
    /// </summary>
    public void ReleaseIcon()
    {
        lock (_gate)
        {
            ReleaseLocked();
        }
    }

    /// <summary>Removes the notification area icon if one is showing and stops showing notifications.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseLocked();
        }
    }

    private void ReleaseLocked()
    {
        _generation++;
        _removal?.Dispose();
        _removal = null;
        RemoveIcon();
    }

    private void ShowLocked(string title, string body)
    {
        var window = _windowHandle();
        if (window == 0)
        {
            return;
        }

        if (_iconWindow != 0 && _iconWindow != window)
        {
            RemoveIcon();
        }

        var icon = _iconFor(window);
        if (_iconWindow == 0 && !AddIcon(window, icon))
        {
            return;
        }

        var balloon = new NotifyIconRequest(NotifyIconCommand.Modify, window, IconId, icon, _appName, title, body);
        if (!Send(balloon))
        {
            // Explorer restarting takes the icon with it: clear whatever is left, add the icon again and retry once.
            RemoveIcon();
            if (!AddIcon(window, icon) || !Send(balloon))
            {
                RemoveIcon();
                return;
            }
        }

        ScheduleRemoval();
    }

    private bool AddIcon(nint window, nint icon)
    {
        if (!Send(new NotifyIconRequest(NotifyIconCommand.Add, window, IconId, icon, _appName, null, null)))
        {
            return false;
        }

        _iconWindow = window;
        return true;
    }

    private void RemoveIcon()
    {
        if (_iconWindow == 0)
        {
            return;
        }

        Send(new NotifyIconRequest(NotifyIconCommand.Delete, _iconWindow, IconId, 0, null, null, null));
        _iconWindow = 0;
    }

    private void ScheduleRemoval()
    {
        var generation = ++_generation;
        _removal?.Dispose();
        _removal = _time.CreateTimer(_ => RemoveWhenDue(generation), null, _iconLifetime, Timeout.InfiniteTimeSpan);
    }

    private void RemoveWhenDue(long generation)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation)
            {
                return;
            }

            RemoveIcon();
        }
    }

    private bool Send(NotifyIconRequest request)
    {
        try
        {
            return _shell.Send(request);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
