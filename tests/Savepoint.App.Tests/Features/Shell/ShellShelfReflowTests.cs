using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;
using Savepoint.App.Theme;
using Savepoint.App.Views;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Shell;

// In the library layout the game cards wrap to the next row when the window narrows, so no card ever ends past the window's edge.
public class ShellShelfReflowTests
{
    private static FakeBackend Library(int games)
    {
        var scenario = ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Layout = ShellLayout.Library });
        foreach (var number in Enumerable.Range(1, games - 3))
        {
            scenario = scenario.WithGame(SampleScenario.Satisfactory with { Id = $"extra-game-{number}", Name = $"Extra game {number}" });
        }

        return new FakeBackend(scenario);
    }

    private static Rect In(ShellViewHarness harness, Visual visual) => LayoutAudit.BoundsIn(harness.Window, visual);

    private static IReadOnlyList<ListBoxItem> Cards(ShellViewHarness harness) =>
        harness.Find<ListBox>("ShelfGames").GetVisualDescendants().OfType<ListBoxItem>().Where(card => card.IsEffectivelyVisible).ToList();

    [AvaloniaTheory]
    [InlineData(640)]
    [InlineData(720)]
    [InlineData(800)]
    [InlineData(900)]
    [InlineData(1040)]
    public async Task Every_card_and_the_add_tile_sit_inside_the_shelf_at_every_window_width(int width)
    {
        using var harness = await ShellViewHarness.StartAsync(Library(9), width: width, height: 660);
        harness.Render($"shelf-wrap-{width}");
        var shelf = In(harness, harness.Find<Border>("Shelf"));
        var cards = Cards(harness);

        Assert.Equal(9, cards.Count);
        Assert.All(cards.Cast<Control>().Append(harness.Find<Button>("ShelfAddTile")), card =>
        {
            var bounds = In(harness, card);
            Assert.True(bounds.Left >= shelf.Left - 0.5 && bounds.Right <= shelf.Right + 0.5, $"A card at {bounds} is outside the shelf at {shelf}.");
        });
    }

    [AvaloniaTheory]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(150)]
    [InlineData(175)]
    [InlineData(200)]
    public async Task Every_card_sits_inside_the_shelf_in_the_smallest_window_at_every_interface_size(int percent)
    {
        var theme = ((App)Application.Current!).Theme;
        var backend = Library(9);
        theme.SetUiScale(percent);
        var main = AppComposition.Create(
            new AppServices(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus, backend.Artwork),
            new UiFormatter(LocalizationCatalog.Current),
            new ThemeServiceApplier(theme),
            () => null,
            new ImmediateUiDispatcher(),
            new ManualTimeProvider());
        var window = new MainWindow { DataContext = main, ScreenSource = () => new MainWindow.ScreenRoom(new PixelRect(0, 0, 3840, 2160), 1.0) };
        try
        {
            window.Show();
            await main.InitializeAsync();
            Dispatcher.UIThread.RunJobs();

            // The tightest case: the window at the smallest size it will go to on this screen.
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            Dispatcher.UIThread.RunJobs();

            var shelf = window.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "Shelf");
            var cards = window.GetVisualDescendants().OfType<ListBoxItem>().Where(card => card.IsEffectivelyVisible && card.FindAncestorOfType<ListBox>()?.Name == "ShelfGames").ToList();
            Assert.Equal(9, cards.Count);
            Assert.All(cards, card =>
            {
                var inShelf = new Rect(card.Bounds.Size).TransformToAABB(card.TransformToVisual(shelf)!.Value);
                Assert.True(inShelf.Left >= -0.5 && inShelf.Right <= shelf.Bounds.Width + 0.5, $"At {percent}% a card spans {inShelf.Left:0} to {inShelf.Right:0} in a shelf {shelf.Bounds.Width:0} wide.");
            });
        }
        finally
        {
            main.Dispose();
            window.Close();
            theme.SetUiScale(UiScale.Default);
        }
    }

    [AvaloniaFact]
    public async Task Cards_drop_to_the_next_row_in_a_narrow_window()
    {
        using var harness = await ShellViewHarness.StartAsync(Library(9), width: 640, height: 660);

        var rows = Cards(harness).Select(card => Math.Round(In(harness, card).Top)).Distinct().ToList();

        Assert.True(rows.Count >= 2, "Every card is still in one row.");
    }

    [AvaloniaTheory]
    [InlineData(640, 420)]
    [InlineData(910, 432)]
    [InlineData(960, 460)]
    [InlineData(1040, 660)]
    public async Task The_shelf_never_scrolls_sideways_and_leaves_the_page_room_to_show(int width, int height)
    {
        using var harness = await ShellViewHarness.StartAsync(Library(40), width: width, height: height);
        harness.Render($"shelf-many-{width}x{height}");

        var sideways = harness.Find<Border>("Shelf").GetVisualDescendants().OfType<ScrollViewer>().Where(scroller => scroller.Extent.Width > scroller.Viewport.Width + 1).ToList();
        var problems = LayoutAudit.Problems(harness.Window);
        var page = In(harness, harness.Find<ContentControl>("ContentRegion"));

        Assert.Empty(sideways);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        Assert.True(page.Height >= height / 3.0, $"The page is left {page.Height:0} px of the {height} px window.");
    }

    [AvaloniaFact]
    public async Task The_arrow_keys_reach_every_card_and_scroll_it_into_view()
    {
        using var harness = await ShellViewHarness.StartAsync(Library(40), width: 640, height: 420);
        var list = harness.Find<ListBox>("ShelfGames");
        var shelf = list.GetVisualAncestors().OfType<ScrollViewer>().First(scroller => scroller.Name != "PART_ScrollViewer");
        list.ContainerFromIndex(0)!.Focus(NavigationMethod.Tab);
        harness.Settle();

        for (var index = 1; index < list.ItemCount; index++)
        {
            harness.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            harness.Window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            harness.Settle();

            var card = In(harness, Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(index)));
            var area = In(harness, shelf);
            Assert.True(card.Top >= area.Top - 1 && card.Bottom <= area.Bottom + 1, $"Card {index} at {card} is outside the shelf's area at {area}.");
        }
    }
}
