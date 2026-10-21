namespace Savepoint.App.Platform;

/// <summary>
/// Raises a notification through the operating system's notification center. The caller decides whether to notify
/// (see <see cref="NotificationPolicy"/>) and supplies text that is already formatted for the user; this layer holds no copy.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Shows a notification and returns at once. Never throws and never blocks on the operating system: when the
    /// notification center is missing or refuses, nothing is shown. Safe to call from any thread.
    /// </summary>
    /// <param name="title">The heading, already localized.</param>
    /// <param name="body">The message, already localized.</param>
    void Show(string title, string body);
}
