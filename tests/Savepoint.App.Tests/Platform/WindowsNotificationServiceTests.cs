using Savepoint.App.Platform.Notifications;
using Savepoint.App.Tests.Platform.Fakes;

namespace Savepoint.App.Tests.Platform;

public class WindowsNotificationServiceTests
{
    private static readonly TimeSpan _lifetime = TimeSpan.FromSeconds(10);

    private nint _window = 0x1000;

    private readonly FakeShellNotifyIcon _shell = new();
    private readonly ManualTimeProvider _time = new();

    private WindowsNotificationService CreateService() =>
        new(() => _window, _shell, window => window + 1, "SavePoint", _lifetime, _time);

    private NotifyIconCommand[] Commands() => [.. _shell.Requests.Select(request => request.Command)];

    [Fact]
    public void The_first_notification_adds_the_icon_then_shows_the_balloon_on_it()
    {
        var service = CreateService();

        service.Show("Capture finished", "2 folders saved");

        Assert.Equal([NotifyIconCommand.Add, NotifyIconCommand.Modify], Commands());
        var add = _shell.Requests[0];
        Assert.Equal(_window, add.WindowHandle);
        Assert.Equal(WindowsNotificationService.IconId, add.Id);
        Assert.Equal(_window + 1, add.Icon);
        Assert.Equal("SavePoint", add.Tip);
        Assert.Null(add.InfoTitle);
        var modify = _shell.Requests[1];
        Assert.Equal(_window, modify.WindowHandle);
        Assert.Equal(WindowsNotificationService.IconId, modify.Id);
        Assert.Equal(_window + 1, modify.Icon);
        Assert.Equal("Capture finished", modify.InfoTitle);
        Assert.Equal("2 folders saved", modify.InfoBody);
    }

    [Fact]
    public void A_later_notification_reuses_the_icon()
    {
        var service = CreateService();

        service.Show("One", "1");
        service.Show("Two", "2");

        Assert.Equal([NotifyIconCommand.Add, NotifyIconCommand.Modify, NotifyIconCommand.Modify], Commands());
        Assert.Equal("Two", _shell.Requests[2].InfoTitle);
    }

    [Fact]
    public void Without_a_window_handle_nothing_is_sent()
    {
        _window = 0;
        var service = CreateService();

        service.Show("Title", "Body");

        Assert.Empty(_shell.Requests);
    }

    [Fact]
    public void The_window_handle_is_looked_up_on_every_notification()
    {
        _window = 0;
        var service = CreateService();
        service.Show("Before the window exists", "x");

        _window = 0x2000;
        service.Show("After", "y");

        Assert.Equal([NotifyIconCommand.Add, NotifyIconCommand.Modify], Commands());
        Assert.Equal(0x2000, _shell.Requests[0].WindowHandle);
    }

    [Fact]
    public void The_icon_goes_away_a_while_after_the_notification()
    {
        var service = CreateService();
        service.Show("Title", "Body");

        _time.Advance(_lifetime - TimeSpan.FromMilliseconds(1));
        var stillThere = _shell.IconCount;
        _time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(1, stillThere);
        Assert.Equal(0, _shell.IconCount);
        Assert.Equal([NotifyIconCommand.Add, NotifyIconCommand.Modify, NotifyIconCommand.Delete], Commands());
        var delete = _shell.Requests[2];
        Assert.Equal(_window, delete.WindowHandle);
        Assert.Equal(WindowsNotificationService.IconId, delete.Id);
    }

    [Fact]
    public void Another_notification_restarts_the_wait_before_the_icon_goes_away()
    {
        var service = CreateService();
        service.Show("One", "1");
        _time.Advance(_lifetime - TimeSpan.FromSeconds(1));

        service.Show("Two", "2");
        _time.Advance(_lifetime - TimeSpan.FromSeconds(1));
        var afterTheFirstDeadline = _shell.IconCount;
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(1, afterTheFirstDeadline);
        Assert.Equal(0, _shell.IconCount);
        Assert.Single(_shell.Requests, request => request.Command == NotifyIconCommand.Delete);
    }

    [Fact]
    public void A_notification_after_the_icon_went_away_adds_it_again()
    {
        var service = CreateService();
        service.Show("One", "1");
        _time.Advance(_lifetime);

        service.Show("Two", "2");

        Assert.Equal(
            [NotifyIconCommand.Add, NotifyIconCommand.Modify, NotifyIconCommand.Delete, NotifyIconCommand.Add, NotifyIconCommand.Modify],
            Commands());
        Assert.Equal(1, _shell.IconCount);
    }

    [Fact]
    public void Disposing_removes_the_icon_and_cancels_the_pending_removal()
    {
        var service = CreateService();
        service.Show("Title", "Body");

        service.Dispose();
        var iconsAtOnce = _shell.IconCount;
        var timersAtOnce = _time.ActiveTimers;
        _time.Advance(_lifetime * 2);

        Assert.Equal(0, iconsAtOnce);
        Assert.Equal(0, timersAtOnce);
        Assert.Single(_shell.Requests, request => request.Command == NotifyIconCommand.Delete);
    }

    [Fact]
    public void Releasing_the_icon_removes_it_at_once_and_cancels_the_pending_removal()
    {
        var service = CreateService();
        service.Show("Title", "Body");

        service.ReleaseIcon();
        var iconsAtOnce = _shell.IconCount;
        var timersAtOnce = _time.ActiveTimers;
        _time.Advance(_lifetime * 2);

        Assert.Equal(0, iconsAtOnce);
        Assert.Equal(0, timersAtOnce);
        Assert.Single(_shell.Requests, request => request.Command == NotifyIconCommand.Delete);
    }

    [Fact]
    public void A_notification_after_releasing_the_icon_adds_it_again()
    {
        var service = CreateService();
        service.Show("One", "1");
        service.ReleaseIcon();

        service.Show("Two", "2");

        Assert.Equal(
            [NotifyIconCommand.Add, NotifyIconCommand.Modify, NotifyIconCommand.Delete, NotifyIconCommand.Add, NotifyIconCommand.Modify],
            Commands());
    }

    [Fact]
    public void Releasing_when_no_icon_is_showing_sends_nothing()
    {
        var service = CreateService();

        service.ReleaseIcon();

        Assert.Empty(_shell.Requests);
    }

    [Fact]
    public void Disposing_without_ever_showing_sends_nothing()
    {
        var service = CreateService();

        service.Dispose();

        Assert.Empty(_shell.Requests);
    }

    [Fact]
    public void Disposing_twice_removes_the_icon_once()
    {
        var service = CreateService();
        service.Show("Title", "Body");

        service.Dispose();
        service.Dispose();

        Assert.Single(_shell.Requests, request => request.Command == NotifyIconCommand.Delete);
    }

    [Fact]
    public void After_disposing_no_notification_is_shown()
    {
        var service = CreateService();
        service.Dispose();

        service.Show("Title", "Body");

        Assert.Empty(_shell.Requests);
    }

    [Fact]
    public void A_refused_add_shows_no_balloon_and_is_tried_again_next_time()
    {
        _shell.Accepts = request => request.Command != NotifyIconCommand.Add;
        var service = CreateService();

        service.Show("One", "1");
        service.Show("Two", "2");

        Assert.Equal([NotifyIconCommand.Add, NotifyIconCommand.Add], Commands());
    }

    [Fact]
    public void An_icon_lost_to_an_Explorer_restart_is_cleared_added_again_and_the_balloon_retried()
    {
        var service = CreateService();
        service.Show("One", "1");
        _shell.ExplorerRestarts();

        service.Show("Two", "2");

        Assert.Equal(
            [NotifyIconCommand.Add, NotifyIconCommand.Modify, NotifyIconCommand.Modify, NotifyIconCommand.Delete, NotifyIconCommand.Add, NotifyIconCommand.Modify],
            Commands());
        Assert.Equal("Two", _shell.Requests[5].InfoTitle);
        Assert.Equal(1, _shell.IconCount);
    }

    [Fact]
    public void A_shell_that_keeps_refusing_leaves_no_icon_behind()
    {
        var service = CreateService();
        service.Show("One", "1");
        _shell.Accepts = request => request.Command == NotifyIconCommand.Delete;

        service.Show("Two", "2");
        service.Dispose();

        Assert.Equal(0, _shell.IconCount);
    }

    [Fact]
    public void A_new_window_gets_its_own_icon_and_the_old_one_is_removed()
    {
        var service = CreateService();
        service.Show("One", "1");

        _window = 0x2000;
        service.Show("Two", "2");

        Assert.Equal(
            [NotifyIconCommand.Add, NotifyIconCommand.Modify, NotifyIconCommand.Delete, NotifyIconCommand.Add, NotifyIconCommand.Modify],
            Commands());
        Assert.Equal(0x1000, _shell.Requests[2].WindowHandle);
        Assert.Equal(0x2000, _shell.Requests[3].WindowHandle);
        Assert.Equal(1, _shell.IconCount);
    }

    [Fact]
    public void A_shell_that_throws_never_reaches_the_caller()
    {
        _shell.Throws = new InvalidOperationException("boom");
        var service = CreateService();

        var error = Record.Exception(() =>
        {
            service.Show("Title", "Body");
            service.Dispose();
        });

        Assert.Null(error);
    }

    [Fact]
    public void A_window_lookup_that_throws_never_reaches_the_caller()
    {
        var service = new WindowsNotificationService(() => throw new InvalidOperationException("boom"), _shell, window => window, "SavePoint", _lifetime, _time);

        var error = Record.Exception(() => service.Show("Title", "Body"));

        Assert.Null(error);
        Assert.Empty(_shell.Requests);
    }

    [Fact]
    public void An_icon_lookup_that_throws_never_reaches_the_caller()
    {
        var service = new WindowsNotificationService(() => _window, _shell, _ => throw new InvalidOperationException("boom"), "SavePoint", _lifetime, _time);

        var error = Record.Exception(() => service.Show("Title", "Body"));

        Assert.Null(error);
        Assert.Empty(_shell.Requests);
    }

    [Fact]
    public async Task Notifications_from_many_threads_never_leave_two_icons_or_a_balloon_without_one()
    {
        var service = CreateService();

        await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Task.Run(
            () =>
            {
                for (var n = 0; n < 50; n++)
                {
                    service.Show($"Title {i}", $"Body {n}");
                }
            },
            TestContext.Current.CancellationToken)));
        service.Dispose();

        var adds = _shell.Requests.Count(request => request.Command == NotifyIconCommand.Add);
        var deletes = _shell.Requests.Count(request => request.Command == NotifyIconCommand.Delete);
        Assert.Equal(1, adds);
        Assert.Equal(1, deletes);
        Assert.Equal(0, _shell.IconCount);
        Assert.Equal(16 * 50, _shell.Requests.Count(request => request.Command == NotifyIconCommand.Modify));
    }
}
