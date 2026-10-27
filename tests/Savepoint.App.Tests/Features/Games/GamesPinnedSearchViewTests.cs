using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Search;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.App.Theme;
using Savepoint.App.Views;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

// The profile search stays above the table while the profiles scroll under it, and scrolls with them when the pane is short.
public class GamesPinnedSearchViewTests
{
    // Enough profiles that the table is taller than any pane these tests use, so it scrolls.
    private static FakeBackend ManyProfiles(ShellLayout layout = ShellLayout.Sidebar)
    {
        var scenario = ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Layout = layout });
        for (var number = 1; number <= 14; number++)
        {
            scenario = scenario.WithProfile(new Profile($"extra-{number}", SampleScenario.SkyrimId, $"Extra run {number}", $"profile/extra-{number}"));
        }

        return new FakeBackend(scenario);
    }

    // The innermost scroll area around the table: the page may have one around the whole screen, which comes first.
    private static ScrollViewer BodyOf(Visual root) => root.GetVisualDescendants().OfType<ScrollViewer>().Last(viewer => viewer.GetVisualDescendants().OfType<Border>().Any(border => border.Name == "Table"));

    private static IEnumerable<SearchBoxView> VisibleBoxes(Visual root) =>
        root.GetVisualDescendants().OfType<SearchBoxView>().Where(box => box.IsEffectivelyVisible && box.Name is "ProfileSearchBox" or "ScrollingProfileSearchBox");

    private static async Task<ComposedApp> StartAsync(double height, ShellLayout layout = ShellLayout.Sidebar) =>
        await ComposedApp.StartWithViewAsync(ManyProfiles(layout), layout: layout, height: height);

    private static void ScrollToEnd(ScrollViewer body)
    {
        body.Offset = new Vector(0, body.Extent.Height);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task In_a_tall_pane_the_search_sits_in_its_own_row_above_the_scrolling_profiles()
    {
        using var app = await StartAsync(height: 660);
        var body = BodyOf(app.View!);

        var box = Assert.Single(VisibleBoxes(app.View!));

        Assert.Equal("ProfileSearchBox", box.Name);
        Assert.DoesNotContain(body, box.GetVisualAncestors());
        Assert.True(box.TranslatePoint(new Point(0, box.Bounds.Height), body)!.Value.Y <= 0.5, "The pinned search overlaps the profiles.");
    }

    [AvaloniaFact]
    public async Task In_a_tall_pane_the_search_stays_in_view_while_the_profiles_scroll()
    {
        using var app = await StartAsync(height: 660);
        var body = BodyOf(app.View!);
        var box = Assert.Single(VisibleBoxes(app.View!));
        var before = box.TranslatePoint(default, app.View!)!.Value.Y;

        ScrollToEnd(body);

        Assert.True(body.Offset.Y > 0, "The profiles did not scroll, so the test proves nothing.");
        Assert.Equal(before, box.TranslatePoint(default, app.View!)!.Value.Y, 1);
    }

    [AvaloniaFact]
    public async Task In_a_short_pane_the_search_is_the_first_thing_in_the_scrolling_profiles()
    {
        using var app = await StartAsync(height: 380);
        var body = BodyOf(app.View!);

        var box = Assert.Single(VisibleBoxes(app.View!));

        Assert.Equal("ScrollingProfileSearchBox", box.Name);
        Assert.Contains(body, box.GetVisualAncestors());
    }

    [AvaloniaFact]
    public async Task In_a_short_pane_the_search_scrolls_away_with_the_profiles()
    {
        using var app = await StartAsync(height: 380);
        var body = BodyOf(app.View!);
        var box = Assert.Single(VisibleBoxes(app.View!));

        ScrollToEnd(body);

        Assert.True(body.Offset.Y > 0, "The profiles did not scroll, so the test proves nothing.");
        Assert.True(box.TranslatePoint(new Point(0, box.Bounds.Height), body)!.Value.Y <= 0, "The search is still in view.");
    }

    // The library layout's top bar and shelf take much of the window, but its header is one row with no chips or footer, so the room that
    // counts is the profiles' own area: it decides, not the window.
    [AvaloniaTheory]
    [InlineData(660, "ProfileSearchBox")]
    [InlineData(500, "ScrollingProfileSearchBox")]
    public async Task The_library_layout_pins_the_search_when_the_profiles_area_has_room(double height, string showing)
    {
        using var app = await StartAsync(height, ShellLayout.Library);

        Assert.Equal([showing], VisibleBoxes(app.View!).Select(box => box.Name));
    }

    [AvaloniaFact]
    public async Task A_pinned_launch_card_that_leaves_the_table_too_little_room_folds_the_search_into_the_scrolling_profiles_until_it_goes()
    {
        using var harness = await GamesViewHarness.StartAsync(ManyProfiles(), height: 480);
        Assert.Equal(["ProfileSearchBox"], VisibleBoxes(harness.View).Select(box => box.Name));

        harness.Rig.LaunchFlow!.SetInline(SampleScenario.SkyrimId, new Border { Height = 150 });
        harness.Settle();

        Assert.True(harness.Find<ContentControl>("InlineContentHost").IsEffectivelyVisible);
        Assert.Equal(["ScrollingProfileSearchBox"], VisibleBoxes(harness.View).Select(box => box.Name));

        harness.Rig.LaunchFlow!.SetInline(SampleScenario.SkyrimId, null);
        harness.Settle();

        Assert.Equal(["ProfileSearchBox"], VisibleBoxes(harness.View).Select(box => box.Name));
    }

    [AvaloniaTheory]
    [InlineData(660, "ProfileSearchBox")]
    [InlineData(380, "ScrollingProfileSearchBox")]
    public async Task Ctrl_F_moves_to_whichever_profile_search_shows(double height, string showing)
    {
        using var app = await StartAsync(height);

        app.Press(Key.F, PhysicalKey.F, RawInputModifiers.Control);

        var field = app.Find<SearchBoxView>(showing).GetVisualDescendants().OfType<TextBox>().Single();
        Assert.Same(field, app.Focused);
    }

    [AvaloniaTheory]
    [InlineData(660, "ProfileSearchBox")]
    [InlineData(380, "ScrollingProfileSearchBox")]
    public async Task The_shown_search_has_the_tooltip_and_the_name_of_the_other(double height, string showing)
    {
        using var app = await StartAsync(height);

        var field = app.Find<SearchBoxView>(showing).GetVisualDescendants().OfType<TextBox>().Single();

        Assert.Equal("Search profiles (Ctrl+F)", ToolTip.GetTip(field));
        Assert.Equal("Search profiles", Avalonia.Automation.AutomationProperties.GetName(field));
    }

    [AvaloniaFact]
    public async Task What_was_typed_survives_the_search_moving_between_the_two_places()
    {
        using var app = await StartAsync(height: 660);
        app.Games.ProfileSearch.Text = "extra";
        app.Settle();

        app.Games.SetBodyHeight(150);
        app.Settle();

        var box = Assert.Single(VisibleBoxes(app.View!));
        Assert.Equal("ScrollingProfileSearchBox", box.Name);
        Assert.Equal("extra", box.GetVisualDescendants().OfType<TextBox>().Single().Text);
    }

    [AvaloniaFact]
    public async Task Focus_in_the_pinned_search_moves_to_the_scrolling_one_when_the_pane_shrinks_and_back_when_it_grows()
    {
        using var app = await StartAsync(height: 660);
        app.Press(Key.F, PhysicalKey.F, RawInputModifiers.Control);
        Assert.Same(FieldOf(app, "ProfileSearchBox"), app.Focused);

        app.Games.SetBodyHeight(150);
        app.Settle();

        Assert.Same(FieldOf(app, "ScrollingProfileSearchBox"), app.Focused);

        app.Games.SetBodyHeight(500);
        app.Settle();

        Assert.Same(FieldOf(app, "ProfileSearchBox"), app.Focused);
    }

    [AvaloniaFact]
    public async Task The_next_keystroke_after_the_search_moves_adds_to_the_text_at_the_caret_instead_of_replacing_it()
    {
        using var app = await StartAsync(height: 660);
        app.Games.ProfileSearch.Text = "extra";
        app.Settle();
        app.Press(Key.F, PhysicalKey.F, RawInputModifiers.Control);
        var pinned = FieldOf(app, "ProfileSearchBox");
        pinned.CaretIndex = 2;
        pinned.SelectionStart = 2;
        pinned.SelectionEnd = 2;

        app.Games.SetBodyHeight(150);
        app.Settle();

        var scrolling = FieldOf(app, "ScrollingProfileSearchBox");
        Assert.Same(scrolling, app.Focused);
        Assert.Equal(2, scrolling.CaretIndex);
        Assert.Equal(scrolling.SelectionStart, scrolling.SelectionEnd);
        app.Window.KeyTextInput("x");
        app.Settle();
        Assert.Equal("exxtra", app.Games.ProfileSearch.Text);
    }

    [AvaloniaFact]
    public async Task A_pane_that_changes_size_leaves_focus_alone_when_the_search_did_not_have_it()
    {
        using var app = await StartAsync(height: 660);
        Assert.NotSame(FieldOf(app, "ProfileSearchBox"), app.Focused);

        app.Games.SetBodyHeight(150);
        app.Settle();

        Assert.NotSame(FieldOf(app, "ScrollingProfileSearchBox"), app.Focused);
    }

    private static TextBox FieldOf(ComposedApp app, string box) => app.Find<SearchBoxView>(box).GetVisualDescendants().OfType<TextBox>().Single();

    [AvaloniaTheory]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(150)]
    [InlineData(175)]
    [InlineData(200)]
    public void The_choice_between_pinned_and_scrolling_does_not_change_with_the_interface_size(int percent)
    {
        var theme = ((App)Application.Current!).Theme;
        var screen = new MainWindow.ScreenRoom(new PixelRect(0, 0, 3840, 2160), 1.0);
        var backend = ManyProfiles();
        theme.SetUiScale(percent);
        var main = AppComposition.Create(
            new AppServices(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus, backend.Artwork),
            new UiFormatter(LocalizationCatalog.Current),
            new ThemeServiceApplier(theme),
            () => null,
            new ImmediateUiDispatcher(),
            new ManualTimeProvider());
        var window = new MainWindow { DataContext = main, ScreenSource = () => screen };
        try
        {
            window.Show();
            main.InitializeAsync().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            // The smallest window at this size holds the same 640x420 of layout as at 100%, so the search scrolls there.
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["ScrollingProfileSearchBox"], VisibleBoxes(window).Select(box => box.Name));

            // A window with the room of a 1040x660 layout pins it.
            window.Width = 1040.0 * percent / 100;
            window.Height = 660.0 * percent / 100;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["ProfileSearchBox"], VisibleBoxes(window).Select(box => box.Name));
        }
        finally
        {
            main.Dispose();
            window.Close();
            theme.SetUiScale(UiScale.Default);
        }
    }
}
