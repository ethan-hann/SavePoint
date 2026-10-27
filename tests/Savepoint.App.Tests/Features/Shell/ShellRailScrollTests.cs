using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.Shell;

// In the sidebar layout the destinations stay put and only the list of games scrolls, in an area of its own whose scrollbar never covers a title.
public class ShellRailScrollTests
{
    private const string _longTitle = "The Extraordinarily Long Title of a Game That Cannot Possibly Fit in the Rail, Special Edition";

    private static FakeBackend ManyGames(int count)
    {
        var scenario = ScenarioBuilder.Sample().WithGame(SampleScenario.Satisfactory with { Id = "long-title", Name = _longTitle });
        foreach (var number in Enumerable.Range(1, count))
        {
            scenario = scenario.WithGame(SampleScenario.Satisfactory with { Id = $"extra-game-{number}", Name = $"Extra game {number}" });
        }

        return new FakeBackend(scenario);
    }

    private static ScrollViewer GamesScroller(ShellViewHarness harness) =>
        harness.Find<ListBox>("RailGames").GetVisualAncestors().OfType<ScrollViewer>().First(scroller => scroller.Name != "PART_ScrollViewer");

    private static Rect In(ShellViewHarness harness, Visual visual) => LayoutAudit.BoundsIn(harness.Window, visual);

    [AvaloniaTheory]
    [InlineData(560)]
    [InlineData(700)]
    public async Task The_destinations_keep_their_place_while_the_list_of_games_scrolls(double height)
    {
        using var harness = await ShellViewHarness.StartAsync(ManyGames(24), height: height);
        var scroller = GamesScroller(harness);
        harness.Render($"rail-scroll-{height}");
        var nav = harness.Find<ListBox>("NavList");
        var brand = harness.Find<StackPanel>("RailBrand");
        var addButton = harness.Find<Button>("RailAddButton");
        var before = (In(harness, nav), In(harness, brand), In(harness, addButton));

        Assert.True(scroller.Extent.Height > scroller.Viewport.Height, "The list of games has to overflow for this check to mean anything.");
        scroller.Offset = new Vector(0, scroller.Extent.Height);
        harness.Settle();

        Assert.True(scroller.Offset.Y > 0, "The list of games did not scroll.");
        Assert.Null(nav.FindAncestorOfType<ScrollViewer>());
        Assert.Equal(before, (In(harness, nav), In(harness, brand), In(harness, addButton)));
    }

    [AvaloniaTheory]
    [InlineData(560, 1040)]
    [InlineData(700, 1040)]
    [InlineData(560, 760)]
    public async Task No_game_title_runs_under_the_scrollbar_of_the_list(double height, double width)
    {
        using var harness = await ShellViewHarness.StartAsync(ManyGames(24), height: height, width: width);
        var scroller = GamesScroller(harness);
        harness.Render($"rail-scrollbar-{width}x{height}");
        var bar = scroller.GetVisualDescendants().OfType<ScrollBar>().Single(candidate => candidate.Orientation == Orientation.Vertical && candidate.FindAncestorOfType<ScrollViewer>() == scroller);
        Assert.True(bar.IsEffectivelyVisible, "The list of games has to show its scrollbar for this check to mean anything.");

        var scrollbarBounds = In(harness, bar);

        // An overlay scrollbar floats over the rows and widens when the pointer nears it; here it has a strip of its own.
        Assert.True(scroller.Viewport.Width <= scroller.Bounds.Width - bar.Bounds.Width + 0.5, $"The rows are {scroller.Viewport.Width:0} wide in a {scroller.Bounds.Width:0} wide area with a {bar.Bounds.Width:0} wide scrollbar.");
        var titles = harness.Find<ListBox>("RailGames").GetVisualDescendants().OfType<TextBlock>().Where(block => block.Classes.Contains("body") && block.IsEffectivelyVisible).ToList();

        Assert.All(titles, title =>
        {
            var bounds = In(harness, title);
            Assert.True(bounds.Right <= scrollbarBounds.Left + 0.5, $"\"{title.Text}\" ends at {bounds.Right:0} and the scrollbar starts at {scrollbarBounds.Left:0}.");
        });

        // The tiles and the highlight of a row end before the scrollbar too, whether or not the names show.
        var rows = harness.Find<ListBox>("RailGames").GetVisualDescendants().OfType<ListBoxItem>().Where(row => row.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.True(In(harness, row).Right <= scrollbarBounds.Left + 0.5, $"A row ends at {In(harness, row).Right:0} and the scrollbar starts at {scrollbarBounds.Left:0}."));
        if (width > 900)
        {
            Assert.Contains(titles, title => title.Text == _longTitle);
        }
    }

    [AvaloniaTheory]
    [InlineData(910, 432)]
    [InlineData(960, 460)]
    [InlineData(1040, 420)]
    public async Task A_scaled_window_keeps_the_destinations_in_view_and_leaves_the_games_room_to_scroll(double width, double height)
    {
        using var harness = await ShellViewHarness.StartAsync(ManyGames(24), height: height, width: width);
        var scroller = GamesScroller(harness);
        harness.Render($"rail-scaled-{width}x{height}");

        var problems = LayoutAudit.Problems(harness.Window);

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        Assert.True(scroller.Viewport.Height >= 96, $"The list of games has {scroller.Viewport.Height:0} px to show its rows in.");
    }

    [AvaloniaFact]
    public async Task The_arrow_keys_reach_every_game_and_bring_it_into_view()
    {
        using var harness = await ShellViewHarness.StartAsync(ManyGames(24), height: 560);
        var scroller = GamesScroller(harness);
        var list = harness.Find<ListBox>("RailGames");
        list.ContainerFromIndex(0)!.Focus(NavigationMethod.Tab);
        harness.Settle();

        for (var index = 1; index < list.ItemCount; index++)
        {
            harness.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            harness.Window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            harness.Settle();

            var item = Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(index));
            var area = In(harness, scroller);
            var shown = In(harness, item);
            Assert.True(shown.Top >= area.Top - 1 && shown.Bottom <= area.Bottom + 1, $"Game {index} at {shown} is outside the list's area at {area}.");
        }
    }
}
