using Avalonia.Threading;
using Savepoint.Core.Diagnostics;

namespace Savepoint.App.Hosting;

/// <summary>What the last window needs to know about a crash.</summary>
/// <param name="Exception">What was thrown.</param>
/// <param name="Origin">Where it was thrown from, in words for the log ("the UI thread", "a background thread").</param>
/// <param name="LogFolder">Where the diagnostic log is, or null when no log could be opened.</param>
public sealed record CrashReport(Exception Exception, string Origin, string? LogFolder);

/// <summary>Shows the user the last window of a crashed app.</summary>
public interface ICrashPresenter
{
    /// <summary>Shows the window and returns once the user has closed it, or at once when no window can be shown.</summary>
    /// <param name="report">The crash to show.</param>
    void Show(CrashReport report);
}

/// <summary>
/// What happens to an exception nothing caught (D34). One thrown on the UI thread or on any other thread is written to the log, shown
/// in a last window, and then the process ends: going on in an unknown state could touch a game's folders wrongly. An exception in a task
/// nobody waited on is written to the log and marked observed, and the app goes on.
/// </summary>
public sealed class CrashHandler
{
    private readonly IDiagnosticLog _log;
    private readonly ICrashPresenter _presenter;
    private readonly Action<int> _exit;
    private readonly string? _logFolder;
    private readonly ManualResetEventSlim _finished = new();

    // The managed thread that is handling a crash, or 0. A second crash must not open a second window or end the process under the first.
    private int _owner;

    /// <summary>Creates the handler.</summary>
    /// <param name="log">Where a crash is written first, before any window.</param>
    /// <param name="presenter">Shows the last window.</param>
    /// <param name="exit">Ends the process with an exit code.</param>
    /// <param name="logFolder">Where the log is, for the window; null when there is none.</param>
    public CrashHandler(IDiagnosticLog log, ICrashPresenter presenter, Action<int> exit, string? logFolder)
    {
        _log = log;
        _presenter = presenter;
        _exit = exit;
        _logFolder = logFolder;
    }

    /// <summary>
    /// Handles an exception that would end the app: writes it to the log, shows the window, then ends the process with exit code 1. A
    /// crash that happens while the window is up is only logged. A crash on another thread waits for the first to finish.
    /// </summary>
    /// <param name="exception">What was thrown.</param>
    /// <param name="origin">Where it came from.</param>
    public void HandleFatal(Exception exception, string origin) => Handle(exception, origin, onWindowThread: false);

    // A crash on the thread that shows the window is only logged while another crash is shown: waiting would freeze the window it waits for.
    private void Handle(Exception exception, string origin, bool onWindowThread)
    {
        TryLog(() => _log.Error($"SavePoint crashed on {origin}: {exception.Message}", exception));

        var me = Environment.CurrentManagedThreadId;
        var owner = Interlocked.CompareExchange(ref _owner, me, 0);
        if (owner == me)
        {
            return;
        }

        if (owner != 0)
        {
            if (!onWindowThread)
            {
                _finished.Wait();
            }

            return;
        }

        try
        {
            _presenter.Show(new CrashReport(exception, origin, _logFolder));
        }
        catch (Exception)
        {
            // The line in the log is what is left to report; the process still has to end.
        }
        finally
        {
            try
            {
                _exit(1);
            }
            finally
            {
                _finished.Set();
            }
        }
    }

    /// <summary>
    /// Handles the exception of a task nobody waited on: it is written to the log and marked observed, so the finalizer thread does not
    /// treat it as unhandled. It is not a crash.
    /// </summary>
    /// <param name="e">The event data.</param>
    public void HandleUnobserved(UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        TryLog(() => _log.Error("A task failed and nothing was waiting for its result.", e.Exception));
    }

    /// <summary>Starts listening for crashes on any thread and for tasks nobody waited on.</summary>
    /// <returns>Stops listening when disposed.</returns>
    public IDisposable AttachToProcess()
    {
        UnhandledExceptionEventHandler domain = (_, e) =>
            HandleFatal(e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString() ?? "An unknown object was thrown."), "a background thread");
        EventHandler<UnobservedTaskExceptionEventArgs> tasks = (_, e) => HandleUnobserved(e);
        AppDomain.CurrentDomain.UnhandledException += domain;
        TaskScheduler.UnobservedTaskException += tasks;
        return new Detach(() =>
        {
            AppDomain.CurrentDomain.UnhandledException -= domain;
            TaskScheduler.UnobservedTaskException -= tasks;
        });
    }

    /// <summary>Starts listening for unhandled exceptions on the UI thread. Call it once the UI framework is running.</summary>
    /// <returns>Stops listening when disposed.</returns>
    public IDisposable AttachToUiThread()
    {
        DispatcherUnhandledExceptionEventHandler ui = (_, e) =>
        {
            // Marked handled first: the window below is shown from inside this callback, and it needs the dispatcher to go on running.
            e.Handled = true;
            Handle(e.Exception, "the UI thread", onWindowThread: true);
        };
        Dispatcher.UIThread.UnhandledException += ui;
        return new Detach(() => Dispatcher.UIThread.UnhandledException -= ui);
    }

    // A log that cannot be written must not stop a crash from being shown.
    private static void TryLog(Action write)
    {
        try
        {
            write();
        }
        catch (Exception)
        {
            // Nowhere left to report it.
        }
    }

    private sealed class Detach(Action detach) : IDisposable
    {
        public void Dispose() => detach();
    }
}
