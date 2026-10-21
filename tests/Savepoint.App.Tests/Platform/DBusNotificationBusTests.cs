using Savepoint.App.Platform.Notifications;
using Savepoint.App.Tests.Platform.Fakes;
using Tmds.DBus.Protocol;

namespace Savepoint.App.Tests.Platform;

public class DBusNotificationBusTests
{
    private static readonly FreedesktopNotification _notification = new("SavePoint", 0, string.Empty, "Title", "Body", [], -1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_login_without_a_session_bus_is_reported_before_any_connection_is_tried()
    {
        var bus = new DBusNotificationBus(() => null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => bus.NotifyAsync(_notification, Ct));
    }

    [Fact]
    public async Task An_empty_session_bus_address_counts_as_none()
    {
        var bus = new DBusNotificationBus(() => string.Empty);

        await Assert.ThrowsAsync<InvalidOperationException>(() => bus.NotifyAsync(_notification, Ct));
    }

    [Fact]
    public async Task An_address_nobody_listens_on_fails_instead_of_hanging()
    {
        var bus = new DBusNotificationBus(() => "unix:abstract=savepoint-nobody-" + Guid.NewGuid().ToString("N"));
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        limit.CancelAfter(TimeSpan.FromSeconds(20));

        var error = await Record.ExceptionAsync(() => bus.NotifyAsync(_notification, limit.Token));

        Assert.NotNull(error);
        Assert.IsNotType<OperationCanceledException>(error);
    }

    [Fact]
    public async Task Every_field_arrives_in_a_real_Notify_call()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux() || PrivateSessionBus.FindDaemon() is null, "Needs Linux and dbus-daemon to run a private session bus.");
        using var session = PrivateSessionBus.TryStart() ?? throw new InvalidOperationException("The private bus did not start.");
        using var server = await FakeNotificationServer.StartAsync(session.Address);
        var bus = new DBusNotificationBus(() => session.Address);
        var notification = new FreedesktopNotification("SavePoint", 0, string.Empty, "Capture \"done\"", "Line one\nSkyrim \u00E9 & friends", [], -1);

        await bus.NotifyAsync(notification, Ct);

        var received = await server.Received.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        Assert.Equal("susssasa{sv}i", received.Signature);
        Assert.Equal("org.freedesktop.Notifications", received.Destination);
        Assert.Equal("/org/freedesktop/Notifications", received.Path);
        Assert.Equal("SavePoint", received.AppName);
        Assert.Equal(0u, received.ReplacesId);
        Assert.Equal(string.Empty, received.AppIcon);
        Assert.Equal("Capture \"done\"", received.Summary);
        Assert.Equal("Line one\nSkyrim \u00E9 & friends", received.Body);
        Assert.Empty(received.Actions);
        Assert.Equal(0, received.HintCount);
        Assert.Equal(-1, received.ExpireTimeoutMilliseconds);
    }

    [Fact]
    public async Task A_bus_with_no_notification_service_answers_with_an_error_and_the_service_skips_the_notification()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux() || PrivateSessionBus.FindDaemon() is null, "Needs Linux and dbus-daemon to run a private session bus.");
        using var session = PrivateSessionBus.TryStart() ?? throw new InvalidOperationException("The private bus did not start.");
        var bus = new DBusNotificationBus(() => session.Address);
        var service = new LinuxNotificationService(bus, "SavePoint", TimeSpan.FromSeconds(20));

        var failure = await Assert.ThrowsAsync<DBusErrorReplyException>(() => bus.NotifyAsync(_notification, Ct));
        var skipped = await Record.ExceptionAsync(() => service.ShowAsync("Title", "Body"));

        Assert.Equal("org.freedesktop.DBus.Error.ServiceUnknown", failure.ErrorName);
        Assert.Null(skipped);
    }
}
