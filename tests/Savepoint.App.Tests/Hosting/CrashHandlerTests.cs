using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Savepoint.App.Hosting;
using Savepoint.Testing;

namespace Savepoint.App.Tests.Hosting;

public class CrashHandlerTests
{
    private const string _logFolder = @"C:\Users\Sam\AppData\Local\SavePoint\Logs";

    private readonly RecordingDiagnosticLog _log = new();
    private readonly FakePresenter _presenter;
    private readonly List<int> _exits = [];

    public CrashHandlerTests()
    {
        _presenter = new FakePresenter(_log, _exits);
    }

    private CrashHandler Handler(string? folder = _logFolder, Action? stopWork = null, Action<CrashReport>? plainNote = null) =>
        new(_log, _presenter, _exits.Add, folder, stopWork, plainNote);

    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException e)
        {
            return e;
        }
    }

    [Fact]
    public void A_crash_is_logged_as_an_error_with_its_exception_and_where_it_came_from()
    {
        var crash = Thrown("the library is gone");

        Handler().HandleFatal(crash, "the UI thread");

        var line = Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("the UI thread", line.Message, StringComparison.Ordinal);
        Assert.Contains("the library is gone", line.Message, StringComparison.Ordinal);
        Assert.Same(crash, line.Exception);
    }

    [Fact]
    public void The_log_has_the_crash_before_the_window_shows_and_the_process_ends_after_it_closes_with_code_one()
    {
        var crash = Thrown("boom");

        Handler().HandleFatal(crash, "a background thread");

        var shown = Assert.Single(_presenter.Shown);
        Assert.Same(crash, shown.Report.Exception);
        Assert.Equal("a background thread", shown.Report.Origin);
        Assert.Equal(_logFolder, shown.Report.LogFolder);
        Assert.Equal(1, shown.ErrorsLoggedWhenShown);
        Assert.Equal([1], _exits);
        Assert.Equal(0, shown.ExitsWhenShown);
    }

    [Fact]
    public void A_plain_note_is_made_once_after_the_crash_is_logged_and_before_the_window_shows()
    {
        var order = new List<string>();
        _presenter.WhileShown = () => order.Add("window");
        var crash = Thrown("boom");
        var handler = Handler(plainNote: report =>
            order.Add($"note for {report.Exception.Message} after {_log.Entries.Count(entry => entry.Level == LogLevel.Error)} logged"));

        handler.HandleFatal(crash, "the UI thread");

        Assert.Equal(["note for boom after 1 logged", "window"], order);
    }

    [Fact]
    public void A_plain_note_that_fails_does_not_keep_the_window_from_showing_or_the_process_from_ending()
    {
        var handler = Handler(plainNote: _ => throw new IOException("the terminal is closed"));

        handler.HandleFatal(Thrown("boom"), "the UI thread");

        Assert.Single(_presenter.Shown);
        Assert.Equal([1], _exits);
    }

    [Fact]
    public void Running_work_is_stopped_after_the_crash_is_logged_and_before_the_window_shows()
    {
        var order = new List<string>();
        _presenter.WhileShown = () => order.Add("window");
        var handler = Handler(stopWork: () => order.Add($"stop after {_log.Entries.Count(entry => entry.Level == LogLevel.Error)} logged"));

        handler.HandleFatal(Thrown("boom"), "a background thread");

        Assert.Equal(["stop after 1 logged", "window"], order);
        Assert.Equal([1], _exits);
    }

    [Fact]
    public void Work_that_cannot_be_stopped_does_not_keep_the_window_from_showing()
    {
        var handler = Handler(stopWork: () => throw new InvalidOperationException("the jobs are gone"));

        handler.HandleFatal(Thrown("boom"), "the UI thread");

        Assert.Single(_presenter.Shown);
        Assert.Equal([1], _exits);
    }

    [Fact]
    public void A_second_crash_does_not_stop_the_work_again()
    {
        var stops = 0;
        var handler = Handler(stopWork: () => stops++);
        _presenter.WhileShown = () => handler.HandleFatal(Thrown("the window itself failed"), "the UI thread");

        handler.HandleFatal(Thrown("first"), "the UI thread");

        Assert.Equal(1, stops);
    }

    [Fact]
    public void A_window_that_cannot_be_shown_does_not_stop_the_process_from_ending()
    {
        _presenter.Throws = true;

        Handler().HandleFatal(Thrown("boom"), "the UI thread");

        Assert.Equal([1], _exits);
        Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public void A_crash_while_the_window_is_up_is_logged_but_does_not_open_another_window_or_end_the_process_twice()
    {
        var handler = Handler();
        _presenter.WhileShown = () => handler.HandleFatal(Thrown("the window itself failed"), "the UI thread");

        handler.HandleFatal(Thrown("first"), "the UI thread");

        Assert.Single(_presenter.Shown);
        Assert.Equal([1], _exits);
        Assert.Equal(2, _log.Entries.Count(entry => entry.Level == LogLevel.Error));
    }

    [Fact]
    public async Task A_second_thread_that_crashes_meanwhile_waits_for_the_first_to_finish_instead_of_ending_the_process_early()
    {
        var handler = Handler();
        using var inWindow = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _presenter.WhileShown = () =>
        {
            inWindow.Set();
            release.Wait(TimeSpan.FromSeconds(30));
        };
        var first = Task.Run(() => handler.HandleFatal(Thrown("first"), "the UI thread"), TestContext.Current.CancellationToken);
        Assert.True(inWindow.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        var second = Task.Run(() => handler.HandleFatal(Thrown("second"), "a background thread"), TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        var finishedEarly = second.IsCompleted;
        release.Set();
        await Task.WhenAll(first, second);

        Assert.False(finishedEarly);
        Assert.Single(_presenter.Shown);
        Assert.Equal([1], _exits);
        Assert.Equal(2, _log.Entries.Count(entry => entry.Level == LogLevel.Error));
    }

    [Fact]
    public void An_unobserved_task_exception_is_logged_and_observed_and_is_not_a_crash()
    {
        var inner = Thrown("nobody waited for this task");
        var args = new UnobservedTaskExceptionEventArgs(new AggregateException(inner));

        Handler().HandleUnobserved(args);

        Assert.True(args.Observed);
        var line = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Contains("nobody waited for this task", line.Exception?.ToString(), StringComparison.Ordinal);
        Assert.Empty(_presenter.Shown);
        Assert.Empty(_exits);
    }

    [Fact]
    public void A_task_nobody_waited_on_that_fails_is_caught_by_the_real_hook()
    {
        using var attached = Handler().AttachToProcess();

        FailInTheBackground();
        // The hook is process-wide, so other tests' abandoned tasks can land in the log first; wait for this one.
        static bool Ours(LogEntry entry) => entry.Exception?.ToString().Contains("failed with nobody watching", StringComparison.Ordinal) == true;
        SpinWait.SpinUntil(
            () =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                return _log.Entries.Any(Ours);
            },
            TimeSpan.FromSeconds(10));

        var line = Assert.Single(_log.Entries, Ours);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Empty(_exits);
    }

    // Its own method, so no local keeps the faulted task alive past the collection that raises the event.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void FailInTheBackground() => _ = Task.Run(() => throw new InvalidOperationException("failed with nobody watching"));

    [AvaloniaFact]
    public void An_exception_on_the_ui_thread_is_logged_shown_and_ends_the_process_instead_of_going_on()
    {
        using var attached = Handler().AttachToUiThread();
        var crash = Thrown("a handler on the UI thread threw");

        Dispatcher.UIThread.Post(() => throw crash);
        var escaped = Record.Exception(() => Dispatcher.UIThread.RunJobs());

        Assert.Null(escaped);
        var line = Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Same(crash, line.Exception);
        Assert.Contains("UI thread", line.Message, StringComparison.Ordinal);
        Assert.Single(_presenter.Shown);
        Assert.Equal([1], _exits);
    }

    // The real window is shown on the UI thread on behalf of a crash from another thread, which waits for it. A crash on the UI thread
    // meanwhile must not wait for that thread too: the window could never be closed and the process would never end.
    [AvaloniaFact]
    public async Task A_crash_on_the_ui_thread_while_a_crash_from_another_thread_is_being_shown_is_logged_and_does_not_wait()
    {
        var handler = Handler();
        using var attached = handler.AttachToUiThread();
        using var inWindow = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _presenter.WhileShown = () =>
        {
            inWindow.Set();
            release.Wait(TimeSpan.FromSeconds(30));
        };
        var first = Task.Run(() => handler.HandleFatal(Thrown("first"), "a background thread"), TestContext.Current.CancellationToken);
        Assert.True(inWindow.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        // A crash that waited would never return on its own, so the first one is let go after a while and the test fails instead of hanging.
        using var safety = new Timer(_ => release.Set(), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        Dispatcher.UIThread.Post(() => throw Thrown("the UI thread failed too"));
        var escaped = Record.Exception(() => Dispatcher.UIThread.RunJobs());
        var firstStillShowing = !release.IsSet;
        release.Set();
        await first;

        Assert.Null(escaped);
        Assert.True(firstStillShowing);
        Assert.Single(_presenter.Shown);
        Assert.Equal([1], _exits);
        Assert.Equal(2, _log.Entries.Count(entry => entry.Level == LogLevel.Error));
    }

    private sealed class FakePresenter(RecordingDiagnosticLog log, List<int> exits) : ICrashPresenter
    {
        public List<Shown> Shown { get; } = [];

        public bool Throws { get; set; }

        public Action? WhileShown { get; set; }

        public void Show(CrashReport report)
        {
            Shown.Add(new Shown(report, log.Entries.Count(entry => entry.Level == LogLevel.Error), exits.Count));
            WhileShown?.Invoke();
            if (Throws)
            {
                throw new InvalidOperationException("no window could be made");
            }
        }
    }

    private sealed record Shown(CrashReport Report, int ErrorsLoggedWhenShown, int ExitsWhenShown);
}
