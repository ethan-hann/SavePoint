using Savepoint.App.Platform.Notifications;

namespace Savepoint.App.Tests.Platform.Fakes;

// Records what would go out on the bus; a test makes it fail or hang to play a missing or stuck notification service.
internal sealed class FakeFreedesktopBus : IFreedesktopNotificationBus
{
    private readonly Func<FreedesktopNotification, CancellationToken, Task> _behavior;

    public FakeFreedesktopBus(Func<FreedesktopNotification, CancellationToken, Task>? behavior = null)
    {
        _behavior = behavior ?? ((_, _) => Task.CompletedTask);
    }

    public List<FreedesktopNotification> Sent { get; } = [];

    public static FakeFreedesktopBus Failing(Exception error) => new((_, _) => Task.FromException(error));

    public static FakeFreedesktopBus Silent() => new(async (_, token) => await Task.Delay(Timeout.Infinite, token));

    public Task NotifyAsync(FreedesktopNotification notification, CancellationToken cancellationToken)
    {
        Sent.Add(notification);
        return _behavior(notification, cancellationToken);
    }
}
