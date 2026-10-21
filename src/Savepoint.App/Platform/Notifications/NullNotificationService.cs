namespace Savepoint.App.Platform.Notifications;

/// <summary>Stands in on an operating system with no notification mechanism, or while a test wants silence.</summary>
public sealed class NullNotificationService : INotificationService
{
    /// <inheritdoc />
    public void Show(string title, string body)
    {
    }
}
