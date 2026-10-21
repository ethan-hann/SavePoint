using Avalonia.Controls;
using Savepoint.App.Platform.Processes;
using Savepoint.Core.Platform;

namespace Savepoint.App.Platform.Notifications;

/// <summary>What the notification services of every OS are built from; tests swap each part for a fake.</summary>
/// <param name="WindowHandle">The main window's native handle, or zero while it has none.</param>
/// <param name="Shell">The Windows notification area.</param>
/// <param name="IconFor">Finds the icon of a window handle.</param>
/// <param name="Runner">Starts helper programs.</param>
/// <param name="Bus">The D-Bus session bus.</param>
/// <param name="Time">Times the removal of the Windows icon.</param>
internal sealed record NotificationParts(
    Func<nint> WindowHandle,
    IShellNotifyIcon Shell,
    Func<nint, nint> IconFor,
    IProcessRunner Runner,
    IFreedesktopNotificationBus Bus,
    TimeProvider Time);

/// <summary>Builds the notification service for the operating system the app runs on.</summary>
public static class NotificationServices
{
    /// <summary>The name notifications appear under.</summary>
    public const string DefaultAppName = "SavePoint";

    /// <summary>How long the Windows notification area icon outlives the latest notification, so the toast has time to appear.</summary>
    internal static readonly TimeSpan IconLifetime = TimeSpan.FromSeconds(15);

    /// <summary>How long a notification waits for the Linux notification service before it is dropped.</summary>
    internal static readonly TimeSpan BusTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Builds the service for this OS. On Windows the notification area icon is removed when the window closes and again
    /// at process exit, so none is left behind. Call it on the UI thread, after the main window is created.
    /// </summary>
    /// <param name="mainWindow">The main window; on Windows its handle carries the notifications.</param>
    /// <param name="appName">The name notifications appear under.</param>
    public static INotificationService Create(Window mainWindow, string appName = DefaultAppName)
    {
        var handles = new WindowHandleSource(mainWindow);
        var parts = new NotificationParts(
            () => handles.Handle,
            new NativeShellNotifyIcon(),
            WindowIcons.For,
            new SystemProcessRunner(),
            DBusNotificationBus.ForCurrentSession(),
            TimeProvider.System);
        var service = Create(PlatformFactory.CurrentOs, appName, parts);
        if (service is WindowsNotificationService windows)
        {
            AttachToWindow(mainWindow, windows);
        }

        return service;
    }

    internal static INotificationService Create(OsKind os, string appName, NotificationParts parts) => os switch
    {
        OsKind.Windows => new WindowsNotificationService(parts.WindowHandle, parts.Shell, parts.IconFor, appName, IconLifetime, parts.Time),
        OsKind.Linux => new LinuxNotificationService(parts.Bus, appName, BusTimeout),
        OsKind.MacOS => new MacNotificationService(parts.Runner),
        _ => new NullNotificationService(),
    };

    /// <summary>
    /// Takes the icon away while the window still exists (the shell keeps the icon of a destroyed window until the mouse
    /// passes over it), ends the service when the window is gone, and does the same at process exit.
    /// </summary>
    internal static void AttachToWindow(Window window, WindowsNotificationService service)
    {
        window.Closing += (_, _) => service.ReleaseIcon();
        window.Closed += (_, _) => service.Dispose();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => service.Dispose();
    }
}
