using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Features.Help;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Games;
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

    [AvaloniaTheory]
    [InlineData(640)]
    [InlineData(720)]
    [InlineData(900)]
    [InlineData(1040)]
    public async Task The_how_it_works_page_never_scrolls_sideways_and_keeps_its_pictures_inside_the_window(int width)
    {
        using var app = await AppStates.OpenAsync("how-it-works");
        app.Window.Width = width;
        app.Settle();
        app.Render("reflow", $"how-it-works-{width}");

        var scroller = app.Find<ScrollViewer>("PageScroll");
        var pictures = app.All<HelpPictureView>().Where(picture => picture.IsEffectivelyVisible).ToList();

        Assert.NotEmpty(pictures);
        Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1, $"At {width}px the page is {scroller.Extent.Width:0}px wide in a {scroller.Viewport.Width:0}px viewport.");

        // A picture is a user control that clips what it draws, so what counts is where its shapes end, not where it does.
        Assert.All(pictures, picture =>
        {
            var farthest = picture.GetVisualDescendants().OfType<Control>().Where(control => control.IsEffectivelyVisible).Max(control => LayoutAudit.BoundsIn(app.Window, control).Right);
            Assert.True(farthest <= app.Window.Bounds.Width, $"A picture draws out to {farthest:0} in a {width}px window.");
        });
    }

    public static TheoryData<int, string> LaunchCardsAtNarrowWidths()
    {
        var data = new TheoryData<int, string>();
        foreach (var width in new[] { 640, 720, 800, 900, 1040 })
        {
            data.Add(width, "banner");
            data.Add(width, "running");
        }

        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(LaunchCardsAtNarrowWidths))]
    public async Task The_launch_banner_and_the_running_card_stay_pinned_above_the_storage_footer_at_narrow_widths(int width, string card)
    {
        using var app = await ComposedApp.StartWithViewAsync(width: width, height: 432);
        await app.Games.Profiles.Single(row => row.Id == MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null).Soon();
        if (card == "running")
        {
            app.Backend.Launch.ReportRunning(MockupScenario.SkyrimId, MockupScenario.RunningSince);
        }

        app.Settle();
        app.Render("reflow", $"launch-{card}-{width}");

        var host = app.Find<ContentControl>("InlineContentHost");
        var footer = app.Find<Border>("StorageFooter");
        Assert.True(host.IsEffectivelyVisible);
        Assert.Equal(12, LayoutAudit.BoundsIn(app.Window, footer).Top - LayoutAudit.BoundsIn(app.Window, host).Bottom, 1.0);

        // The card has its own place, so the table is not scrolled away to make room for it.
        var scroller = app.Find<Border>("Table").GetVisualAncestors().OfType<ScrollViewer>().First();
        Assert.Equal(0, scroller.Offset.Y);
        var problems = LayoutAudit.Problems(app.Window);
        Assert.True(problems.Count == 0, $"At {width}px: {string.Join("; ", problems)}");
    }
}
