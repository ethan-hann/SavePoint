using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Savepoint.App.Hosting;

namespace Savepoint.App.Tests.Hosting;

public class AvaloniaCrashPresenterTests
{
    private static CrashReport Report() => new(new InvalidOperationException("the library is gone"), "the UI thread", @"C:\Logs");

    // The test runs on the UI thread, so work posted to it only runs when the test lets the dispatcher run. A window that is up keeps
    // a nested loop going inside the pump, so each test closes its window from a timer instead of waiting for a later line of the test.
    private static void PumpUntil(Func<bool> done, TimeSpan limit)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.Elapsed < limit)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }

    private static Action<CrashWindow> CloseSoon(List<CrashWindow> shown) => window =>
    {
        lock (shown)
        {
            shown.Add(window);
        }

        DispatcherTimer.RunOnce(window.Close, TimeSpan.FromMilliseconds(100));
    };

    [AvaloniaFact]
    public void On_the_ui_thread_the_window_is_shown_and_the_call_returns_when_it_is_closed()
    {
        var shown = new List<CrashWindow>();
        var presenter = new AvaloniaCrashPresenter(_ => Task.FromResult(true), shown: CloseSoon(shown));

        presenter.Show(Report());

        var window = Assert.Single(shown);
        Assert.False(window.IsVisible);
        Assert.Equal("SavePoint", window.Title);
    }

    [AvaloniaFact]
    public void From_another_thread_the_window_is_shown_on_the_ui_thread_and_the_call_waits_until_it_is_closed()
    {
        var events = new List<string>();
        void Record(string what)
        {
            lock (events)
            {
                events.Add(what);
            }
        }

        var presenter = new AvaloniaCrashPresenter(_ => Task.FromResult(true), shown: window =>
        {
            Record("shown");
            window.Closed += (_, _) => Record("closed");
            DispatcherTimer.RunOnce(window.Close, TimeSpan.FromMilliseconds(150));
        });

        var call = Task.Run(
            () =>
            {
                presenter.Show(Report());
                Record("returned");
            },
            TestContext.Current.CancellationToken);
        PumpUntil(() => call.IsCompleted, TimeSpan.FromSeconds(30));

        Assert.True(call.IsCompleted, "The call should have returned once the window closed.");
        Assert.Equal(["shown", "closed", "returned"], events);
    }

    [AvaloniaFact]
    public void A_ui_thread_that_never_gets_to_it_does_not_hold_the_crash_up_and_shows_nothing_later()
    {
        var shown = new List<CrashWindow>();
        var presenter = new AvaloniaCrashPresenter(_ => Task.FromResult(true), uiStartTimeout: TimeSpan.FromMilliseconds(200), shown: CloseSoon(shown));

        var call = Task.Run(() => presenter.Show(Report()), TestContext.Current.CancellationToken);
        Assert.True(call.Wait(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken), "The call should give up by itself.");
        PumpUntil(() => shown.Count > 0, TimeSpan.FromSeconds(1));

        Assert.Empty(shown);
    }

    [AvaloniaFact]
    public void The_window_is_shown_modal_to_the_owner_it_is_given()
    {
        var owner = new Window();
        owner.Show();
        var shown = new List<CrashWindow>();
        var presenter = new AvaloniaCrashPresenter(_ => Task.FromResult(true), () => owner, shown: CloseSoon(shown));

        presenter.Show(Report());

        Assert.Single(shown);
        Assert.True(owner.IsVisible);
        owner.Close();
    }
}
