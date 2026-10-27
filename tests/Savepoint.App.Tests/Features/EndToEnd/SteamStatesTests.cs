using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common.Errors;

namespace Savepoint.App.Tests.Features.EndToEnd;

// The layout audits only mean something when a state really shows the card it is named for.
public class SteamStatesTests
{
    [AvaloniaTheory]
    [InlineData("launch-steam-card", "Steam needs to be running", 3)]
    [InlineData("launch-steam-waiting", "Waiting for Steam to start…", 1)]
    [InlineData("launch-checking-closed", "Checking the game has closed…", 0)]
    public async Task Each_Steam_state_shows_its_card_with_its_buttons(string state, string title, int buttons)
    {
        using var app = await AppStates.OpenAsync(state);
        app.Settle();

        var card = Assert.IsType<ErrorCardViewModel>(app.Games.InlineContent);
        Assert.Equal(title, card.Title);
        Assert.Equal(buttons, card.Actions.Count);
    }
}
