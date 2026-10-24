using Tmds.DBus.Protocol;

namespace Savepoint.App.Platform.Notifications;

/// <summary>Calls <c>org.freedesktop.Notifications.Notify</c> on the D-Bus session bus.</summary>
internal sealed class DBusNotificationBus : IFreedesktopNotificationBus, IDisposable
{
    private const string _service = "org.freedesktop.Notifications";
    private const string _path = "/org/freedesktop/Notifications";

    private readonly Func<string?> _sessionAddress;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DBusConnection? _connection;
    private bool _disposed;

    /// <param name="sessionAddress">Finds the session bus address; null or empty when this login has none.</param>
    public DBusNotificationBus(Func<string?> sessionAddress)
    {
        _sessionAddress = sessionAddress;
    }

    /// <summary>The bus of the current login: the address D-Bus clients find in <c>DBUS_SESSION_BUS_ADDRESS</c>.</summary>
    public static DBusNotificationBus ForCurrentSession() => new(() => DBusAddress.Session);

    /// <summary>Closes the connection, which also closes the notifications still showing on desktops that tie them to it.</summary>
    public void Dispose()
    {
        _gate.Wait();
        try
        {
            _disposed = true;
            Drop();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task NotifyAsync(FreedesktopNotification notification, CancellationToken cancellationToken)
    {
        var address = _sessionAddress();
        if (string.IsNullOrEmpty(address))
        {
            throw new InvalidOperationException("This login has no D-Bus session bus.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // GNOME closes the notifications of an app that has a window when the connection that sent them goes away,
            // so the connection stays open for the life of the app and is reused.
            if (_connection is null)
            {
                var fresh = new DBusConnection(address);
                try
                {
                    await fresh.ConnectAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    fresh.Dispose();
                    throw;
                }

                _connection = fresh;
            }

            await _connection.CallMethodAsync(BuildCall(_connection, notification), ReadId, null).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DBusErrorReplyException)
        {
            // The service answered, so the connection is fine; it is just not offering notifications.
            throw;
        }
        catch
        {
            // A connection that failed or stalled is not trusted again; the next notification connects afresh.
            Drop();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Drop()
    {
        _connection?.Dispose();
        _connection = null;
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
