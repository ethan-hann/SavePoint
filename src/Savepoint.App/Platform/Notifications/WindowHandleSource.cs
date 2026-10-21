using Avalonia.Controls;
using Avalonia.Platform;

namespace Savepoint.App.Platform.Notifications;

/// <summary>
/// Keeps the native handle of a window where any thread can read it: Avalonia only hands it out on the UI thread, and a
/// notification is raised from whichever thread finished a job. Create it on the UI thread.
/// </summary>
internal sealed class WindowHandleSource
{
    private nint _handle;

    public WindowHandleSource(Window window)
    {
        _handle = HandleOf(window.TryGetPlatformHandle());
        window.Opened += (_, _) => Volatile.Write(ref _handle, HandleOf(window.TryGetPlatformHandle()));
        window.Closed += (_, _) => Volatile.Write(ref _handle, 0);
    }

    /// <summary>The window's handle, or zero while it has none or is not a Windows window.</summary>
    public nint Handle => Volatile.Read(ref _handle);

    /// <summary>The handle when it is an <c>HWND</c>, which is the only kind the notification area accepts; otherwise zero.</summary>
    public static nint HandleOf(IPlatformHandle? handle) =>
        handle is { HandleDescriptor: "HWND" } ? handle.Handle : 0;
}
