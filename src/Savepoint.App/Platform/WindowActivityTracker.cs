using Avalonia;
using Avalonia.Controls;

namespace Savepoint.App.Platform;

/// <summary>
/// Watches a window's activation and window state and keeps a copy that any thread can read, because job events arrive
/// on background threads while Avalonia properties may only be read on the UI thread. Create it on the UI thread.
/// </summary>
public sealed class WindowActivityTracker : IWindowActivitySource, IDisposable
{
    private const int _activeBit = 1;
    private const int _minimizedBit = 2;

    private readonly Window _window;
    // Both flags in one int so a reader on another thread never sees one of them from a different moment.
    private int _bits;
    private bool _active;
    private bool _closed;
    private bool _disposed;

    /// <param name="window">The window to watch; its current state is read at once.</param>
    public WindowActivityTracker(Window window)
    {
        _window = window;
        _active = window.IsActive;
        _bits = Pack(Read());
        window.Activated += OnActivated;
        window.Deactivated += OnDeactivated;
        window.Closed += OnClosed;
        window.PropertyChanged += OnPropertyChanged;
    }

    /// <inheritdoc />
    public WindowActivity Current
    {
        get
        {
            var bits = Volatile.Read(ref _bits);
            return new WindowActivity((bits & _activeBit) != 0, (bits & _minimizedBit) != 0);
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Stops watching the window.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.Activated -= OnActivated;
        _window.Deactivated -= OnDeactivated;
        _window.Closed -= OnClosed;
        _window.PropertyChanged -= OnPropertyChanged;
    }

    private static int Pack(WindowActivity activity) =>
        (activity.IsActive ? _activeBit : 0) | (activity.IsMinimized ? _minimizedBit : 0);

    private WindowActivity Read() => new(_active && !_closed, _window.WindowState == WindowState.Minimized);

    private void OnActivated(object? sender, EventArgs e)
    {
        _active = true;
        Refresh();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        _active = false;
        Refresh();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        Refresh();
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var bits = Pack(Read());
        if (bits == Volatile.Read(ref _bits))
        {
            return;
        }

        Volatile.Write(ref _bits, bits);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
