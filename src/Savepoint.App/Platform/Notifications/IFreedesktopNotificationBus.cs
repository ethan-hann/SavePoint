namespace Savepoint.App.Platform.Notifications;

/// <summary>The arguments of <c>org.freedesktop.Notifications.Notify</c>.</summary>
/// <param name="AppName">The application name the notification center groups by.</param>
/// <param name="ReplacesId">The id of a notification to replace, or 0 for a new one.</param>
/// <param name="AppIcon">An icon name or path, or empty for none.</param>
/// <param name="Summary">The heading.</param>
/// <param name="Body">The message.</param>
/// <param name="Actions">Alternating action ids and labels; empty when the notification has no buttons.</param>
/// <param name="ExpireTimeoutMilliseconds">How long to show it, 0 for never expire, or -1 for the server's default.</param>
internal sealed record FreedesktopNotification(
    string AppName,
    uint ReplacesId,
    string AppIcon,
    string Summary,
    string Body,
    IReadOnlyList<string> Actions,
    int ExpireTimeoutMilliseconds);

/// <summary>The D-Bus session bus as far as notifications need it, behind a seam so tests never touch a real bus.</summary>
internal interface IFreedesktopNotificationBus
{
    /// <summary>Sends the notification and completes when the notification service has accepted it.</summary>
    /// <param name="notification">What to show.</param>
    /// <param name="cancellationToken">Gives up on a bus that does not answer.</param>
    /// <exception cref="InvalidOperationException">There is no session bus to talk to.</exception>
    /// <exception cref="Exception">Any failure to reach or get an answer from the notification service.</exception>
    Task NotifyAsync(FreedesktopNotification notification, CancellationToken cancellationToken);
}
