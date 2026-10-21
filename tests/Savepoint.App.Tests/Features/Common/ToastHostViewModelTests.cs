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
    public void A_warning_or_error_toast_stays_for_ten_seconds(ToastKind kind)
    {
        var host = Host();
        host.Show(new ToastMessage("Trouble.", kind));

        _clock.Advance(TimeSpan.FromSeconds(9.9));
        Assert.Single(host.Toasts);

        _clock.Advance(TimeSpan.FromSeconds(0.2));
        Assert.Empty(host.Toasts);
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

    private sealed class QueueingDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _queued = [];

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
