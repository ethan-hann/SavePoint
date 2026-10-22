using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Text and cards give way to a narrow window or a long translation instead of being cut off.
public class ReflowTests
{
    [AvaloniaFact]
    public async Task A_long_storage_path_on_the_first_run_screen_wraps_inside_the_window()
    {
        var path = @"D:\Users\someone-with-a-long-name\Documents\Programs and their data\SavePoint's own storage folder for everything it keeps";
        var backend = new FakeBackend(ScenarioBuilder.Empty().WithSettings(AppSettings.Defaults(path)));
        using var app = await ComposedApp.StartWithViewAsync(backend, width: 640, height: 660);
        app.Settle();
        app.Render("reflow", "first-run-long-path");

        var note = app.Find<Border>("StorageNote");
        var text = note.GetVisualDescendants().OfType<TextBlock>().First(block => block.Inlines?.Count > 0);

        Assert.True(LayoutAudit.BoundsIn(app.Window, note).Right <= app.Window.Bounds.Width, $"The note ends at {LayoutAudit.BoundsIn(app.Window, note).Right:0} in a {app.Window.Bounds.Width:0}px window.");
        Assert.True(text.Bounds.Height > 24, $"The note stays on one line ({text.Bounds.Height:0}px high).");
    }

    [AvaloniaTheory]
    [InlineData(640)]
    [InlineData(720)]
    [InlineData(800)]
    [InlineData(900)]
    [InlineData(1040)]
    public async Task History_cards_drop_to_fewer_columns_before_they_get_cramped(int width)
    {
        using var app = await AppStates.OpenAsync("history-cards");
        app.Window.Width = width;
        app.Settle();
        app.Render("reflow", $"history-cards-{width}");

        var cards = app.All<UniformGrid>().Where(grid => grid.IsEffectivelyVisible && grid.Rows == 0).SelectMany(grid => grid.Children).ToList();
        Assert.NotEmpty(cards);
        var narrowest = cards.Min(card => card.Bounds.Width);
        Assert.True(narrowest >= 200, $"At {width}px the narrowest card is {narrowest:0}px wide.");
    }
}
