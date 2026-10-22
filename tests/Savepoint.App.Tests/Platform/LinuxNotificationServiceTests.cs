using System.Diagnostics;
using Savepoint.App.Platform.Notifications;
using Savepoint.App.Tests.Platform.Fakes;

namespace Savepoint.App.Tests.Platform;

public class LinuxNotificationServiceTests
{
    private static readonly TimeSpan _patient = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task The_notification_is_sent_under_the_app_name_with_the_callers_text()
    {
        var bus = new FakeFreedesktopBus();
        var service = new LinuxNotificationService(bus, "SavePoint", _patient);

        await service.ShowAsync("Capture finished", "Skyrim: 2 folders saved");

        var sent = Assert.Single(bus.Sent);
        Assert.Equal("SavePoint", sent.AppName);
        Assert.Equal("Capture finished", sent.Summary);
        Assert.Equal("Skyrim: 2 folders saved", sent.Body);
    }

    [Fact]
    public async Task The_notification_is_new_iconless_buttonless_and_uses_the_servers_default_timeout()
    {
        var bus = new FakeFreedesktopBus();
        var service = new LinuxNotificationService(bus, "SavePoint", _patient);

        await service.ShowAsync("Title", "Body");

        var sent = Assert.Single(bus.Sent);
        Assert.Equal(0u, sent.ReplacesId);
        Assert.Equal(string.Empty, sent.AppIcon);
        Assert.Empty(sent.Actions);
        Assert.Equal(-1, sent.ExpireTimeoutMilliseconds);
    }

    [Fact]
    public async Task The_app_name_is_whatever_the_caller_configured()
    {
        var bus = new FakeFreedesktopBus();
        var service = new LinuxNotificationService(bus, "Another Name", _patient);

        await service.ShowAsync("Title", "Body");

        Assert.Equal("Another Name", Assert.Single(bus.Sent).AppName);
    }

    [Fact]
    public async Task Markup_in_the_text_is_escaped_so_a_game_name_cannot_draw_a_link_or_a_tag()
    {
        var bus = new FakeFreedesktopBus();
        var service = new LinuxNotificationService(bus, "SavePoint", _patient);

        await service.ShowAsync("A & B", "Saved <a href=\"https://example.invalid\">Skyrim</a> & <b>more</b>");

        var sent = Assert.Single(bus.Sent);
        Assert.Equal("A &amp; B", sent.Summary);
        Assert.Equal("Saved &lt;a href=\"https://example.invalid\"&gt;Skyrim&lt;/a&gt; &amp; &lt;b&gt;more&lt;/b&gt;", sent.Body);
    }

    [Fact]
    public async Task A_NUL_character_cannot_travel_in_a_D_Bus_string_so_it_becomes_a_space()
    {
        var bus = new FakeFreedesktopBus();
        var service = new LinuxNotificationService(bus, "SavePoint", _patient);

        await service.ShowAsync("a\0b", "c\0d");

        var sent = Assert.Single(bus.Sent);
        Assert.Equal("a b", sent.Summary);
        Assert.Equal("c d", sent.Body);
    }

    [Fact]
    public void Show_sends_the_notification_without_waiting_for_the_bus()
    {
        var bus = new FakeFreedesktopBus();
        var service = new LinuxNotificationService(bus, "SavePoint", _patient);

        service.Show("Title", "Body");

        Assert.Single(bus.Sent);
    }

    [Fact]
    public async Task No_notification_service_means_nothing_is_shown_and_nothing_is_thrown()
    {
        var bus = FakeFreedesktopBus.Failing(new InvalidOperationException("no session bus"));
        var service = new LinuxNotificationService(bus, "SavePoint", _patient);

        var error = await Record.ExceptionAsync(() => service.ShowAsync("Title", "Body"));

        Assert.Null(error);
        Assert.Single(bus.Sent);
    }

    [Fact]
    public void Show_does_not_throw_when_the_bus_fails_to_connect()
    {
        var bus = FakeFreedesktopBus.Failing(new IOException("connection refused"));
        var service = new LinuxNotificationService(bus, "SavePoint", _patient);

        var error = Record.Exception(() => service.Show("Title", "Body"));

        Assert.Null(error);
        Assert.Single(bus.Sent);
    }

    [Fact]
    public async Task A_bus_that_never_answers_is_given_up_on_after_the_timeout()
    {
        var bus = FakeFreedesktopBus.Silent();
        var service = new LinuxNotificationService(bus, "SavePoint", TimeSpan.FromMilliseconds(100));
        var watch = Stopwatch.StartNew();

        var error = await Record.ExceptionAsync(() => service.ShowAsync("Title", "Body"));

        Assert.Null(error);
        Assert.Single(bus.Sent);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"the attempt lasted {watch.Elapsed}");
    }
}
