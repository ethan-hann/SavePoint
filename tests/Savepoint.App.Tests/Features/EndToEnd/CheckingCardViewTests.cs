using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Press Play and the screen answers at once, however long the scan of both folders takes.
public class CheckingCardViewTests
{
    [AvaloniaFact]
    public async Task Play_shows_the_checking_card_with_a_moving_bar_and_a_working_cancel_until_the_scan_returns()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var release = new TaskCompletionSource();
        app.Backend.Launch.PreviewGate = token => release.Task.WaitAsync(token);

        var play = app.Games.Profiles.Single(row => row.Id == MockupScenario.SurvivalId).PlayCommand.ExecuteAsync(null);
        app.Settle();

        var card = app.All<LaunchCheckingView>().Single();
        var texts = card.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToList();
        Assert.Contains("Checking your files…", texts);
        Assert.Contains("This can take a moment on a large game.", texts);
        Assert.True(app.Find<ProgressBar>("Bar").IsIndeterminate);
        var cancel = app.Find<Button>("CancelButton");
        Assert.Equal("Cancel", AutomationAudit.NameOf(cancel));
        Assert.Empty(AutomationAudit.Problems(card));

        cancel.Command!.Execute(null);
        await play.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        app.Settle();

        Assert.Empty(app.All<LaunchCheckingView>());
        Assert.Empty(app.Toasts.Toasts);
    }

    [AvaloniaFact]
    public async Task Save_them_first_shows_a_bar_with_the_files_done_where_its_buttons_were()
    {
        using var app = await AppStates.OpenAsync("launch-dialog-saving");

        var bar = app.All<StackPanel>().Single(panel => panel.Name == "SavingProgress" && panel.IsEffectivelyVisible).GetVisualDescendants().OfType<ProgressBar>().Single();
        Assert.Equal(41, bar.Value, 0.5);
        Assert.False(bar.IsIndeterminate);
        Assert.Equal("Taking a snapshot of these changes…", AutomationAudit.NameOf(bar));
        var texts = app.All<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToList();
        Assert.Contains("410 of 1,000 files", texts);
        Assert.DoesNotContain(app.All<Button>(), button => button.IsEffectivelyVisible && button.Content as string is "Take a snapshot first" or "Discard them");
        app.Render("launch", "2b-launch-dialog-saving-first");
    }
}
