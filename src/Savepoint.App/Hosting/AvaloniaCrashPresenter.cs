using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Localization;

namespace Savepoint.App.Hosting;

/// <summary>
/// Shows the crash window with Avalonia, on the UI thread, whichever thread crashed. It returns once the window is closed, so the
/// crashed thread cannot end the process before the user has read it.
/// </summary>
public sealed class AvaloniaCrashPresenter : ICrashPresenter
{
    private const int _waiting = 0;
    private const int _started = 1;
    private const int _abandoned = 2;

    private readonly Func<string, Task<bool>> _openFolder;
    private readonly Func<Window?> _owner;
    private readonly TimeSpan _uiStartTimeout;
    private readonly Action<CrashWindow>? _shown;

    /// <summary>Creates the presenter.</summary>
    /// <param name="openFolder">Opens a folder in the file manager for the window's button; answers false when it could not.</param>
    /// <param name="owner">The window the crash window is modal to, so nothing else in the app can be clicked; the app's main window when null.</param>
    /// <param name="uiStartTimeout">How long a crash on another thread waits for the UI thread to start showing the window before it gives up; five seconds when null.</param>
    /// <param name="shown">Called with the window once it is on screen; for tests.</param>
    public AvaloniaCrashPresenter(Func<string, Task<bool>> openFolder, Func<Window?>? owner = null, TimeSpan? uiStartTimeout = null, Action<CrashWindow>? shown = null)
    {
        _openFolder = openFolder;
        _owner = owner ?? MainWindow;
        _uiStartTimeout = uiStartTimeout ?? TimeSpan.FromSeconds(5);
        _shown = shown;
    }

    /// <inheritdoc />
    public void Show(CrashReport report)
    {
        // A crash before the UI framework exists has no window to show; the log has it.
        if (Application.Current is null)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            ShowAndWait(report);
            return;
        }

        var state = _waiting;
        using var closed = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        Dispatcher.UIThread.Post(() =>
        {
            // A UI thread that was too busy to answer in time may get here long after the process has gone on to end.
            if (Interlocked.CompareExchange(ref state, _started, _waiting) != _waiting)
            {
                return;
            }

            started.Set();
            try
            {
                ShowAndWait(report);
            }
            finally
            {
                closed.Set();
            }
        });

        if (!started.Wait(_uiStartTimeout) && Interlocked.CompareExchange(ref state, _abandoned, _waiting) == _waiting)
        {
            return;
        }

        closed.Wait();
    }

    private static Window? MainWindow() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    // The crash may predate the catalog, so the window loads its own copy when the app has none.
    private static UiFormatter Formatter()
    {
        try
        {
            return new UiFormatter(LocalizationCatalog.Current);
        }
        catch (InvalidOperationException)
        {
            return new UiFormatter(LocalizationCatalog.LoadEmbedded("en"));
        }
    }

    private void ShowAndWait(CrashReport report)
    {
        var window = new CrashWindow(Formatter(), report, _openFolder);
        var frame = new DispatcherFrame();
        window.Closed += (_, _) => frame.Continue = false;
        if (_owner() is { IsVisible: true } owner)
        {
            _ = window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }

        _shown?.Invoke(window);

        // Waiting on a nested loop keeps the window alive while the callback that crashed is still on the stack.
        Dispatcher.UIThread.PushFrame(frame);
    }
}
