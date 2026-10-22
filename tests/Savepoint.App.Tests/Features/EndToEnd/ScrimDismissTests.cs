using Avalonia.Headless.XUnit;

namespace Savepoint.App.Tests.Features.EndToEnd;

// A click on the scrim, which is where a player lands on the way back from a game, closes only a dialog with nothing to lose.
public class ScrimDismissTests
{
    [AvaloniaTheory]
    [InlineData("games-delete-profile", true)]
    [InlineData("games-banner-details", true)]
    [InlineData("first-run-templates", true)]
    [InlineData("editor-consent", true)]
    [InlineData("games-delete-game", false)]
    [InlineData("games-edit-profile", false)]
    [InlineData("games-duplicate-profile", false)]
    [InlineData("games-new-profile-wizard", false)]
    [InlineData("launch-dialog", false)]
    [InlineData("launch-dialog-unsaved", false)]
    [InlineData("launch-dialog-keep-as-new", false)]
    [InlineData("launch-post-play", false)]
    [InlineData("launch-post-play-older-point", false)]
    [InlineData("settings-clear-all", false)]
    [InlineData("editor-reconnect", false)]
    public async Task A_dialog_ends_on_a_scrim_click_only_when_it_holds_nothing_to_lose(string state, bool closes)
    {
        using var app = await AppStates.OpenAsync(state);
        var dialog = app.Main.Dialogs.Current;
        Assert.NotNull(dialog);

        app.Main.Dialogs.RequestScrimDismiss();
        app.Settle();

        Assert.Equal(closes, dialog.IsClosed);
    }
}
