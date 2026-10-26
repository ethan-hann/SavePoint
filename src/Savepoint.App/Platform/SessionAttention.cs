using Savepoint.App.Features.Common.Formatting;

namespace Savepoint.App.Platform;

/// <summary>
/// The player-facing half of the session end: when the window is not in front, flash the taskbar and post a desktop
/// notification saying the game closed and SavePoint is waiting for an answer.
/// </summary>
public sealed class SessionAttention : ISessionAttention
{
    private readonly IWindowActivitySource _window;
    private readonly INotificationService _notifications;
    private readonly IWindowFlasher _flasher;
    private readonly UiFormatter _text;

    /// <param name="window">Says whether the window is in front of the player.</param>
    /// <param name="notifications">Raises the desktop notification.</param>
    /// <param name="flasher">Draws attention to the window without activating it.</param>
    /// <param name="text">Formats the notification.</param>
    public SessionAttention(IWindowActivitySource window, INotificationService notifications, IWindowFlasher flasher, UiFormatter text)
    {
        _window = window;
        _notifications = notifications;
        _flasher = flasher;
        _text = text;
    }

    /// <inheritdoc />
    public void SessionEnded(string gameName)
    {
        var state = _window.Current;
        if (state.IsActive && !state.IsMinimized)
        {
            return;
        }

        _flasher.Flash();
        _notifications.Show(_text.Message("app.name"), _text.Message("launch.postPlay.attention.body", ("gameName", gameName)));
    }
}
