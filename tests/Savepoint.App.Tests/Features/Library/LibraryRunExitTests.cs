using Savepoint.App.Features.Library;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;

namespace Savepoint.App.Tests.Features.Library;

// Closing the window while games are being added asks first, like a running job does (D31).
public class LibraryRunExitTests
{
    // A close that did not wait for the run would finish within moments, and only waiting shows that it does not.
    private static readonly TimeSpan _closeHeldWindow = TimeSpan.FromMilliseconds(300);

    private static (ExitGuard Guard, FakeDialogs Dialogs, LibraryRunTracker Tracker) Build()
    {
        var backend = FakeBackend.Empty();
        var dialogs = new FakeDialogs();
        var tracker = new LibraryRunTracker();
        return (new ExitGuard(backend.Jobs, dialogs, LibraryTestData.Text, TimeSpan.FromMilliseconds(100), tracker), dialogs, tracker);
    }

    [Fact]
    public async Task With_no_run_going_the_window_closes_without_a_question()
    {
        var (guard, dialogs, _) = Build();

        Assert.True(await guard.PrepareToCloseAsync());

        Assert.Empty(dialogs.Confirmations);
    }

    [Fact]
    public async Task Staying_open_leaves_the_run_alone()
    {
        var (guard, dialogs, tracker) = Build();
        var stops = 0;
        using var run = tracker.Begin(() => stops++);
        dialogs.AnswerConfirm(false);

        var allowed = await guard.PrepareToCloseAsync();

        Assert.False(allowed);
        Assert.Equal(0, stops);
        var asked = Assert.Single(dialogs.Confirmations);
        Assert.Equal("SavePoint is still working", asked.Title);
        Assert.Contains("adding your games", asked.Message, StringComparison.Ordinal);
        Assert.Equal("Stay open", asked.CancelLabel);
    }

    [Fact]
    public async Task Quitting_asks_the_run_to_stop_and_waits_for_the_game_it_is_on()
    {
        var (guard, dialogs, tracker) = Build();
        var stops = 0;
        var run = tracker.Begin(() => stops++);
        dialogs.AnswerConfirm(true);

        var closing = guard.PrepareToCloseAsync();
        await Task.Delay(_closeHeldWindow, TestContext.Current.CancellationToken);
        var waitedWhileRunning = !closing.IsCompleted;
        run.Dispose();
        var allowed = await closing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(waitedWhileRunning);
        Assert.True(allowed);
        Assert.Equal(1, stops);
    }

    [Fact]
    public async Task A_run_that_ends_while_the_question_is_open_lets_the_window_close()
    {
        var (guard, dialogs, tracker) = Build();
        var run = tracker.Begin(() => { });
        dialogs.AnswerConfirm(true);
        dialogs.WhileConfirming = _ =>
        {
            run.Dispose();
            return Task.CompletedTask;
        };

        Assert.True(await guard.PrepareToCloseAsync());
    }

    [Fact]
    public async Task A_real_run_is_known_to_the_tracker_from_its_first_game_to_its_last()
    {
        var tracker = new LibraryRunTracker();
        var backend = FakeBackend.Empty();
        var games = new[] { LibraryTestData.Game("A", id: "1"), LibraryTestData.Game("B", id: "2") };
        backend.State.Library = LibraryTestData.Scan(games);
        var dialog = new LibraryAddDialogViewModel(backend.Games, games, LibraryTestData.Text, tracker);
        var seen = new List<bool>();
        dialog.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(dialog.ProgressText))
            {
                seen.Add(tracker.IsRunning);
            }
        };

        await dialog.RunAsync();

        Assert.NotEmpty(seen);
        Assert.All(seen.Take(2), running => Assert.True(running));
        Assert.False(tracker.IsRunning);
    }

    [Fact]
    public async Task Asking_a_real_run_to_stop_leaves_the_games_after_the_current_one_untried()
    {
        var tracker = new LibraryRunTracker();
        var backend = FakeBackend.Empty();
        var games = new[] { LibraryTestData.Game("A", id: "1"), LibraryTestData.Game("B", id: "2"), LibraryTestData.Game("C", id: "3") };
        backend.State.Library = LibraryTestData.Scan(games);
        var dialog = new LibraryAddDialogViewModel(backend.Games, games, LibraryTestData.Text, tracker);
        dialog.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(dialog.ProgressText) && dialog.ProgressText.StartsWith("Adding 1 of", StringComparison.Ordinal))
            {
                tracker.RequestStop();
            }
        };

        await dialog.RunAsync();
        dialog.CloseCommand.Execute(null);

        var summary = await dialog.Result;
        Assert.Single(summary.Results);
        Assert.Equal(2, summary.NotTried);
    }
}
