namespace Savepoint.App.Platform.Notifications;

/// <summary>Posts notifications to the desktop's notification service over D-Bus; does nothing when none is running.</summary>
internal sealed class LinuxNotificationService : INotificationService
{
    private readonly IFreedesktopNotificationBus _bus;
    private readonly string _appName;
    private readonly TimeSpan _timeout;

    /// <param name="bus">Carries the call to the session bus.</param>
    /// <param name="appName">The name the notification center shows and groups by.</param>
    /// <param name="timeout">How long to wait for the notification service before giving up.</param>
    public LinuxNotificationService(IFreedesktopNotificationBus bus, string appName, TimeSpan timeout)
    {
        _bus = bus;
        _appName = appName;
        _timeout = timeout;
    }

    /// <inheritdoc />
    public void Show(string title, string body) => _ = ShowAsync(title, body);

    /// <summary>Sends the notification and completes when the service answered or the attempt was given up; never faults.</summary>
    public async Task ShowAsync(string title, string body)
    {
        try
        {
            var notification = new FreedesktopNotification(_appName, 0, string.Empty, Safe(title), Safe(body), [], -1);
            using var limit = new CancellationTokenSource(_timeout);
            await _bus.NotifyAsync(notification, limit.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // No session bus, no notification service, or one that does not answer: the notification is simply skipped.
        }
    }

    // A D-Bus string cannot hold NUL, and a message that does is rejected as a whole. The body is read as markup (links, tags),
    // so a game name goes in escaped; the summary gets the same treatment since some servers read it that way too.
    private static string Safe(string text) =>
        text.Replace('\0', ' ').Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
}
