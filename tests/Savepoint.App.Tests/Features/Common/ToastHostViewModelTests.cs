using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Theme;

namespace Savepoint.App.Tests.Features.Common;

public class ToastHostViewModelTests
{
    private readonly ManualTimeProvider _clock = new();

    private ToastHostViewModel Host(IUiDispatcher? dispatcher = null) => new(_clock, dispatcher ?? new ImmediateUiDispatcher());

    [Fact]
    public void A_new_host_shows_nothing()
    {
        var host = Host();

        Assert.Empty(host.Toasts);
        Assert.Equal(string.Empty, host.Announcement);
    }

    [Fact]
    public void Showing_a_toast_lists_it_with_its_text_details_and_kind_icon()
    {
        var host = Host();

        var toast = host.Show(new ToastMessage("Saved Skyrim.", ToastKind.Success, "3 files"));

        Assert.Same(toast, Assert.Single(host.Toasts));
        Assert.Equal("Saved Skyrim.", toast.Text);
        Assert.Equal("3 files", toast.Details);
        Assert.True(toast.HasDetails);
        Assert.True(toast.IsSuccess);
        Assert.Equal(Icons.CheckCircle, toast.Icon);
    }

    [Theory]
    [InlineData(ToastKind.Success, "CheckCircle")]
    [InlineData(ToastKind.Info, "Info")]
    [InlineData(ToastKind.Warning, "Warning")]
    [InlineData(ToastKind.Error, "Error")]
    public void Each_kind_has_its_own_icon_and_flag(ToastKind kind, string icon)
    {
        var toast = Host().Show(new ToastMessage("x", kind));

        var expected = icon switch
        {
            "CheckCircle" => Icons.CheckCircle,
            "Info" => Icons.Info,
            "Warning" => Icons.Warning,
            _ => Icons.Error,
        };
        Assert.Equal(expected, toast.Icon);
        Assert.Equal(kind == ToastKind.Success, toast.IsSuccess);
        Assert.Equal(kind == ToastKind.Warning, toast.IsWarning);
        Assert.Equal(kind == ToastKind.Error, toast.IsError);
    }

    [Fact]
    public void A_toast_without_details_has_none()
    {
        var toast = Host().Show(new ToastMessage("Saved."));

        Assert.False(toast.HasDetails);
    }

    [Fact]
    public void Showing_a_toast_publishes_its_text_for_the_live_region()
    {
        var host = Host();

        host.Show(new ToastMessage("Saved Skyrim.", ToastKind.Success));

        Assert.Equal("Saved Skyrim.", host.Announcement);

        host.Show(new ToastMessage("Something went wrong saving Skyrim.", ToastKind.Error, "The disk is full."));

        Assert.Equal("Something went wrong saving Skyrim. The disk is full.", host.Announcement);
    }

    [Fact]
    public void A_success_toast_leaves_after_six_seconds()
    {
        var host = Host();
        host.Show(new ToastMessage("Saved.", ToastKind.Success));

        _clock.Advance(TimeSpan.FromSeconds(5.9));
        Assert.Single(host.Toasts);

        _clock.Advance(TimeSpan.FromSeconds(0.2));
        Assert.Empty(host.Toasts);
    }

    [Theory]
    [InlineData(ToastKind.Warning)]
    [InlineData(ToastKind.Error)]
    public void A_warning_or_error_toast_stays_until_it_is_dismissed(ToastKind kind)
    {
        var host = Host();
        host.Show(new ToastMessage("Trouble.", kind));

        _clock.Advance(TimeSpan.FromHours(1));

        Assert.Single(host.Toasts);
        Assert.Equal(0, _clock.ActiveTimers);
    }

    [Fact]
    public void An_error_that_names_its_duration_still_leaves_on_time()
    {
        var host = Host();
        host.Show(new ToastMessage("Trouble.", ToastKind.Error, Duration: TimeSpan.FromSeconds(30)));

        _clock.Advance(TimeSpan.FromSeconds(31));

        Assert.Empty(host.Toasts);
    }

    [Fact]
    public void Newer_toasts_never_push_out_an_error_nobody_has_dismissed()
    {
        var host = Host();
        var error = host.Show(new ToastMessage("Could not delete.", ToastKind.Error));

        var others = Enumerable.Range(1, 5).Select(number => host.Show(new ToastMessage($"Toast {number}"))).ToList();

        Assert.Contains(error, host.Toasts);
        Assert.Equal([error, others[2], others[3], others[4]], host.Toasts);
    }

    [Fact]
    public void The_same_problem_shown_twice_is_listed_once_and_announced_again()
    {
        var host = Host();
        var first = host.Show(new ToastMessage("Could not delete.", ToastKind.Error, "The disk is full."));
        host.Show(new ToastMessage("Another.", ToastKind.Info));
        host.Announcement = string.Empty;

        var second = host.Show(new ToastMessage("Could not delete.", ToastKind.Error, "The disk is full."));

        Assert.Same(first, second);
        Assert.Equal(2, host.Toasts.Count);
        Assert.Equal("Could not delete. The disk is full.", host.Announcement);
    }

    [Fact]
    public void Pausing_holds_every_timer_and_resuming_gives_back_the_time_that_was_left()
    {
        var host = Host();
        host.Show(new ToastMessage("Saved.", ToastKind.Success));
        _clock.Advance(TimeSpan.FromSeconds(1));

        host.SetPaused(true);
        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.True(host.IsPaused);
        Assert.Single(host.Toasts);
        Assert.Equal(0, _clock.ActiveTimers);

        host.SetPaused(false);
        _clock.Advance(TimeSpan.FromSeconds(4.9));
        Assert.Single(host.Toasts);

        _clock.Advance(TimeSpan.FromSeconds(0.2));
        Assert.Empty(host.Toasts);
    }

    [Fact]
    public void A_toast_that_was_nearly_gone_when_paused_gets_at_least_three_seconds_back()
    {
        var host = Host();
        host.Show(new ToastMessage("Saved.", ToastKind.Success));
        _clock.Advance(TimeSpan.FromSeconds(5.5));

        host.SetPaused(true);
        host.SetPaused(false);
        _clock.Advance(TimeSpan.FromSeconds(2.9));
        Assert.Single(host.Toasts);

        _clock.Advance(TimeSpan.FromSeconds(0.2));
        Assert.Empty(host.Toasts);
    }

    [Fact]
    public void A_toast_that_arrives_while_paused_waits_to_start_its_timer()
    {
        var host = Host();
        host.SetPaused(true);
        host.Show(new ToastMessage("Saved.", ToastKind.Success));

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Single(host.Toasts);

        host.SetPaused(false);
        _clock.Advance(TimeSpan.FromSeconds(6.1));
        Assert.Empty(host.Toasts);
    }

    [Fact]
    public void Dismissing_all_clears_every_toast_and_timer()
    {
        var host = Host();
        host.Show(new ToastMessage("Saved.", ToastKind.Success));
        host.Show(new ToastMessage("Could not delete.", ToastKind.Error));
        Assert.True(host.HasMany);

        host.DismissAll();

        Assert.Empty(host.Toasts);
        Assert.False(host.HasMany);
        Assert.Equal(0, _clock.ActiveTimers);
    }

    [Fact]
    public void A_toast_can_name_its_own_duration()
    {
        var host = Host();
        host.Show(new ToastMessage("Quick.", ToastKind.Info, Duration: TimeSpan.FromSeconds(2)));

        _clock.Advance(TimeSpan.FromSeconds(2.1));

        Assert.Empty(host.Toasts);
    }

    [Fact]
    public void Dismissing_a_toast_removes_it_at_once_and_its_timer_goes_quiet()
    {
        var host = Host();
        var toast = host.Show(new ToastMessage("Saved."));

        toast.DismissCommand.Execute(null);

        Assert.Empty(host.Toasts);
        Assert.Equal(0, _clock.ActiveTimers);
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Empty(host.Toasts);
    }

    [Fact]
    public void Dismissing_a_toast_that_is_already_gone_does_nothing()
    {
        var host = Host();
        var toast = host.Show(new ToastMessage("Saved."));
        host.Dismiss(toast);

        host.Dismiss(toast);

        Assert.Empty(host.Toasts);
    }

    [Fact]
    public void Only_the_newest_toasts_stay_on_screen()
    {
        var host = Host();

        var toasts = Enumerable.Range(1, 5).Select(number => host.Show(new ToastMessage($"Toast {number}"))).ToList();

        Assert.Equal(ToastHostViewModel.MaxVisible, host.Toasts.Count);
        Assert.Equal([toasts[2], toasts[3], toasts[4]], host.Toasts);
        Assert.Equal(ToastHostViewModel.MaxVisible, _clock.ActiveTimers);
    }

    [Fact]
    public void Each_toast_has_its_own_timer()
    {
        var host = Host();
        host.Show(new ToastMessage("First", ToastKind.Success));
        _clock.Advance(TimeSpan.FromSeconds(4));
        host.Show(new ToastMessage("Second", ToastKind.Success));

        _clock.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(["Second"], host.Toasts.Select(toast => toast.Text));
    }

    [Fact]
    public void The_timer_hands_its_dismissal_to_the_ui_dispatcher()
    {
        var dispatcher = new QueueingDispatcher();
        var host = Host(dispatcher);
        host.Show(new ToastMessage("Saved.", ToastKind.Success));

        _clock.Advance(TimeSpan.FromSeconds(7));

        Assert.Single(host.Toasts);
        dispatcher.RunAll();
        Assert.Empty(host.Toasts);
    }

    [Fact]
    public void A_disposed_host_posts_nothing_when_a_timer_runs_out()
    {
        var dispatcher = new QueueingDispatcher();
        var host = Host(dispatcher);
        host.Show(new ToastMessage("Saved.", ToastKind.Success));

        host.Dispose();
        _clock.Advance(TimeSpan.FromSeconds(7));

        Assert.Equal(0, dispatcher.Count);
    }

    [Fact]
    public void A_dismissal_posted_before_disposal_does_nothing_when_it_runs_late()
    {
        var dispatcher = new QueueingDispatcher();
        var host = Host(dispatcher);
        host.Show(new ToastMessage("Saved.", ToastKind.Success));
        _clock.Advance(TimeSpan.FromSeconds(7));
        Assert.Equal(1, dispatcher.Count);

        host.Dispose();
        dispatcher.RunAll();

        Assert.Single(host.Toasts);
    }

    [Fact]
    public void A_toast_shown_after_disposal_starts_no_timer()
    {
        var dispatcher = new QueueingDispatcher();
        var host = Host(dispatcher);
        host.Dispose();

        host.Show(new ToastMessage("Saved.", ToastKind.Success));
        _clock.Advance(TimeSpan.FromSeconds(7));

        Assert.Equal(0, dispatcher.Count);
    }

    [Fact]
    public void Releasing_a_pause_after_disposal_starts_no_timer()
    {
        var dispatcher = new QueueingDispatcher();
        var host = Host(dispatcher);
        host.Show(new ToastMessage("Saved.", ToastKind.Success));
        host.SetPaused(true);

        host.Dispose();
        host.SetPaused(false);
        _clock.Advance(TimeSpan.FromSeconds(7));

        Assert.Equal(0, dispatcher.Count);
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        var host = Host();
        host.Show(new ToastMessage("Saved.", ToastKind.Success));

        host.Dispose();

        var exception = Record.Exception(host.Dispose);
        Assert.Null(exception);
    }

    private sealed class QueueingDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _queued = [];

        public int Count => _queued.Count;

        public void Post(Action action) => _queued.Enqueue(action);

        public void RunAll()
        {
            while (_queued.TryDequeue(out var action))
            {
                action();
            }
        }
    }
}
