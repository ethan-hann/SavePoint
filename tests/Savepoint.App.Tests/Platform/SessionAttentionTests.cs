using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Localization;
using Savepoint.App.Platform;

namespace Savepoint.App.Tests.Platform;

public class SessionAttentionTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows");

    private sealed class FakeWindow(bool active, bool minimized) : IWindowActivitySource
    {
        public WindowActivity Current { get; } = new(active, minimized);

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class Recorder : INotificationService, IWindowFlasher
    {
        public List<(string Title, string Body)> Shown { get; } = [];

        public int Flashes { get; private set; }

        public void Show(string title, string body) => Shown.Add((title, body));

        public void Flash() => Flashes++;
    }

    private static Recorder EndWith(bool active, bool minimized, string game = "Balatro")
    {
        var recorder = new Recorder();
        new SessionAttention(new FakeWindow(active, minimized), recorder, recorder, _text).SessionEnded(game);
        return recorder;
    }

    [Fact]
    public void A_window_behind_another_one_flashes_and_posts_a_notification_naming_the_game()
    {
        var recorder = EndWith(active: false, minimized: false);

        Assert.Equal(1, recorder.Flashes);
        var shown = Assert.Single(recorder.Shown);
        Assert.Equal("SavePoint", shown.Title);
        Assert.Equal("Balatro closed. Open SavePoint to choose what to keep from this session.", shown.Body);
    }

    [Fact]
    public void A_minimized_window_that_still_reports_active_is_called_back_too()
    {
        var recorder = EndWith(active: true, minimized: true);

        Assert.Equal(1, recorder.Flashes);
        Assert.Single(recorder.Shown);
    }

    [Fact]
    public void A_window_in_front_of_the_player_is_left_alone()
    {
        var recorder = EndWith(active: true, minimized: false);

        Assert.Equal(0, recorder.Flashes);
        Assert.Empty(recorder.Shown);
    }

    [Fact]
    public void The_relay_passes_the_call_to_whatever_target_is_set_and_ignores_it_before_then()
    {
        var relay = new SessionAttentionRelay();
        relay.SessionEnded("Before");
        var target = new Tests.Fakes.RecordingAttention();
        relay.Target = target;

        relay.SessionEnded("Balatro");

        Assert.Equal(["Balatro"], target.Ended);
    }
}
