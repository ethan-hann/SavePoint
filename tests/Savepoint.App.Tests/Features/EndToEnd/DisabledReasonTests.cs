using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace Savepoint.App.Tests.Features.EndToEnd;

// A greyed-out button that says nothing teaches the rules by trial and error, so Play says why it is off.
public class DisabledReasonTests
{
    private const string _reason = "This game is already running. Play works again after you close it.";

    private const string _busyReason = "A switch or snapshot for this game is not finished yet. Play works again when it is.";

    private static List<Button> PlayButtons(ComposedApp app) => [.. app.All<Button>().Where(button => button.Name == "PlayButton")];

    [AvaloniaFact]
    public async Task Play_says_why_it_is_off_while_a_game_is_running_and_the_tip_shows_on_a_disabled_button()
    {
        using var app = await AppStates.OpenAsync("games-running");
        app.Settle();

        var buttons = PlayButtons(app);

        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.False(button.IsEffectivelyEnabled));
        Assert.All(buttons, button => Assert.Equal(_reason, ToolTip.GetTip(button)));
        Assert.All(buttons, button => Assert.True(ToolTip.GetShowOnDisabled(button)));
    }

    [AvaloniaTheory]
    [InlineData("launch-dialog")]
    [InlineData("launch-progress")]
    public async Task Play_does_not_claim_the_game_is_running_while_a_switch_is_waiting_or_running(string state)
    {
        using var app = await AppStates.OpenAsync(state);
        app.Settle();

        var buttons = PlayButtons(app);

        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.False(button.IsEffectivelyEnabled));
        Assert.All(buttons, button => Assert.Equal(_busyReason, ToolTip.GetTip(button)));
    }

    [AvaloniaFact]
    public async Task Play_carries_no_tip_while_it_works()
    {
        using var app = await AppStates.OpenAsync("games-sidebar");

        Assert.All(PlayButtons(app), button => Assert.Null(ToolTip.GetTip(button)));
    }
}
