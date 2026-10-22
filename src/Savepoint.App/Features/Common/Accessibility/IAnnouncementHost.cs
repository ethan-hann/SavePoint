using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Savepoint.App.Features.Common.Accessibility;

/// <summary>
/// A part of the window that owns a live region which is always there, so a message put into it is spoken. Something that
/// appears on screen cannot announce itself, because a screen reader only follows changes to elements it already knows.
/// </summary>
public interface IAnnouncementHost
{
    /// <summary>Speaks a message, even one identical to the previous message.</summary>
    /// <param name="message">What to say.</param>
    void Announce(string message);
}

/// <summary>Helpers for saying that something has appeared.</summary>
public static class Announcements
{
    /// <summary>
    /// Says a message through the window's <see cref="IAnnouncementHost"/> once the control has been laid out; does nothing
    /// when the control is not inside one.
    /// </summary>
    /// <param name="source">The control that appeared.</param>
    /// <param name="message">What to say, such as a card's title and body.</param>
    public static void Appeared(Control source, string message)
    {
        if (message.Length == 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (source.GetVisualAncestors().OfType<IAnnouncementHost>().FirstOrDefault() is { } host)
                {
                    host.Announce(message);
                }
            },
            DispatcherPriority.Loaded);
    }
}
