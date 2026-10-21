using Tmds.DBus.Protocol;

namespace Savepoint.App.Platform.Notifications;

/// <summary>Calls <c>org.freedesktop.Notifications.Notify</c> on the D-Bus session bus.</summary>
internal sealed class DBusNotificationBus : IFreedesktopNotificationBus
{
    private const string _service = "org.freedesktop.Notifications";
    private const string _path = "/org/freedesktop/Notifications";

    private readonly Func<string?> _sessionAddress;

    /// <param name="sessionAddress">Finds the session bus address; null or empty when this login has none.</param>
    public DBusNotificationBus(Func<string?> sessionAddress)
    {
        _sessionAddress = sessionAddress;
    }

    /// <summary>The bus of the current login: the address D-Bus clients find in <c>DBUS_SESSION_BUS_ADDRESS</c>.</summary>
    public static DBusNotificationBus ForCurrentSession() => new(() => DBusAddress.Session);

    /// <inheritdoc />
    public async Task NotifyAsync(FreedesktopNotification notification, CancellationToken cancellationToken)
    {
        var address = _sessionAddress();
        if (string.IsNullOrEmpty(address))
        {
            throw new InvalidOperationException("This login has no D-Bus session bus.");
        }

        // One short-lived connection per notification: they are rare, and nothing is left open to leak at exit.
        using var connection = new DBusConnection(address);
        await connection.ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        await connection.CallMethodAsync(BuildCall(connection, notification), ReadId, null).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static MessageBuffer BuildCall(DBusConnection connection, FreedesktopNotification notification)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: _service,
            path: _path,
            @interface: _service,
            member: "Notify",
            signature: "susssasa{sv}i");
        writer.WriteString(notification.AppName);
        writer.WriteUInt32(notification.ReplacesId);
        writer.WriteString(notification.AppIcon);
        writer.WriteString(notification.Summary);
        writer.WriteString(notification.Body);
        writer.WriteArray(notification.Actions.ToArray());
        writer.WriteDictionary(new Dictionary<string, VariantValue>());
        writer.WriteInt32(notification.ExpireTimeoutMilliseconds);
        return writer.CreateMessage();
    }

    // The notification id is of no use: nothing replaces or closes a notification later.
    private static uint ReadId(Message message, object? state) => message.GetBodyReader().ReadUInt32();
}
