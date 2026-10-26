using System.Runtime.InteropServices;
using Avalonia.Controls;
using Savepoint.App.Platform.Notifications;

namespace Savepoint.App.Platform;

/// <summary>Builds the window flasher for the operating system the app runs on.</summary>
public static class WindowFlashers
{
    /// <summary>
    /// Windows flashes the taskbar button until the window is brought forward; Linux and macOS have no cue Avalonia can
    /// ask for, so they get a flasher that does nothing and rely on the desktop notification. Call it on the UI thread.
    /// </summary>
    /// <param name="window">The main window.</param>
    public static IWindowFlasher For(Window window) =>
        OperatingSystem.IsWindows() ? new WindowsWindowFlasher(new WindowHandleSource(window)) : new NoWindowFlasher();

    private sealed class NoWindowFlasher : IWindowFlasher
    {
        public void Flash()
        {
        }
    }

    // FlashWindowEx only draws attention; unlike activating the window it cannot pull the focus from a running game.
    private sealed class WindowsWindowFlasher(WindowHandleSource handles) : IWindowFlasher
    {
        private const uint _flashTray = 2;
        private const uint _flashUntilForeground = 12;

        public void Flash()
        {
            var handle = handles.Handle;
            if (!OperatingSystem.IsWindows() || handle == 0)
            {
                return;
            }

            var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = handle, Flags = _flashTray | _flashUntilForeground, Count = uint.MaxValue };
            _ = FlashWindowEx(ref info);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FlashInfo
        {
            public uint Size;
            public nint Window;
            public uint Flags;
            public uint Count;
            public uint Timeout;
        }

        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlashWindowEx(ref FlashInfo info);
    }
}
