using Savepoint.App.Platform;

namespace Savepoint.App.Tests.Platform;

public class X11WindowRaiserTests
{
    [Fact]
    public void The_request_is_a_NET_ACTIVE_WINDOW_message_for_the_window()
    {
        var message = X11WindowRaiser.MessageFor(0x140000f);

        Assert.Equal("_NET_ACTIVE_WINDOW", message.MessageType);
        Assert.Equal(0x140000f, message.Window);
        Assert.Equal(32, message.Format);
    }

    // The window manager refuses a raise stamped with a time older than the user's last input, and the only time this
    // process knows is its own last event; a request stamped "now" is what a second launch is.
    [Fact]
    public void The_request_comes_from_an_application_and_is_stamped_now()
    {
        var message = X11WindowRaiser.MessageFor(0x140000f);

        Assert.Equal(1, message.SourceIndication);
        Assert.Equal(0, message.Timestamp);
        Assert.Equal(0, message.CurrentActiveWindow);
    }

    [Fact]
    public void Raising_without_a_window_or_an_X_display_reports_failure_instead_of_throwing()
    {
        Assert.False(X11WindowRaiser.TryRaise(0));
    }
}
