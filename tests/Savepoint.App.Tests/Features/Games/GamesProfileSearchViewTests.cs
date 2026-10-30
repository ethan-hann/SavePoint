using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Search;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.Games;

public class GamesProfileSearchViewTests
{
    private static TextBox Field(ComposedApp app) => app.Find<SearchBoxView>("ProfileSearchBox").GetVisualDescendants().OfType<TextBox>().Single();

    private static List<string> Rows(ComposedApp app) =>
        [.. app.Find<ItemsControl>("Rows").GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("table-row")).Select(border => AutomationAudit.NameOf(border)!.Split(". ")[0])];

    private static void Type(ComposedApp app, string text)
    {
        app.Window.KeyTextInput(text);
        app.Settle();
    }

    [AvaloniaFact]
    public async Task The_search_sits_above_the_table_and_has_a_name_a_screen_reader_can_say()
    {
        using var app = await ComposedApp.StartWithViewAsync();

        var field = Field(app);

        Assert.True(field.IsEffectivelyVisible);
        Assert.Equal("Search profiles", AutomationAudit.NameOf(field));
        Assert.True(field.TranslatePoint(default, app.Find<Border>("Table"))!.Value.Y < 0, "The search box is not above the table.");
        Assert.Empty(AutomationAudit.Problems(app.View!));
    }

    [AvaloniaFact]
    public async Task Ctrl_F_on_the_Games_screen_moves_to_the_profile_search_not_the_games_search()
    {
        using var app = await ComposedApp.StartWithViewAsync();

        app.Press(Key.F, PhysicalKey.F, RawInputModifiers.Control);

        Assert.Same(Field(app), app.Focused);
    }

    [AvaloniaFact]
    public async Task Typing_lists_only_the_profiles_that_match()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        Field(app).Focus();

        Type(app, "survival");

        Assert.Equal(["Survival test - 5-14-26"], Rows(app));
    }

    [AvaloniaFact]
    public async Task While_a_search_narrows_the_table_its_header_says_how_many_profiles_show()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var line = app.All<TextBlock>().FirstOrDefault(block => block.Name == "ProfileCountLine");
        Assert.NotNull(line);
        Assert.False(line.IsEffectivelyVisible);
        Field(app).Focus();

        Type(app, "survival");

        Assert.True(line.IsEffectivelyVisible);
        Assert.Equal("1 of 3 profiles", line.Text);
        Assert.Contains(app.Find<Border>("TableHeader"), line.GetVisualAncestors());

        app.Press(Key.Escape, PhysicalKey.Escape);

        Assert.False(line.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Escape_clears_the_search_and_every_profile_is_back()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        Field(app).Focus();
        Type(app, "survival");

        app.Press(Key.Escape, PhysicalKey.Escape);

        Assert.Equal(3, Rows(app).Count);
        Assert.Same(Field(app), app.Focused);
    }

    [AvaloniaFact]
    public async Task A_search_nothing_matches_hides_the_table_and_says_so_with_a_way_to_clear_it()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        Field(app).Focus();
        Type(app, "zzz");
        var line = app.Find<StackPanel>("NoProfileMatches");

        Assert.False(app.Find<Border>("Table").IsEffectivelyVisible);
        Assert.True(Field(app).IsEffectivelyVisible);
        Assert.True(line.IsEffectivelyVisible);
        Assert.Equal("No profiles match.", line.GetVisualDescendants().OfType<TextBlock>().First().Text);

        new ButtonAutomationPeer(line.GetVisualDescendants().OfType<Button>().Single()).Invoke();
        app.Settle();

        Assert.True(app.Find<Border>("Table").IsEffectivelyVisible);
        Assert.False(line.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Choosing_another_game_empties_the_box()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        Field(app).Focus();
        Type(app, "survival");

        app.Shell.SelectGame(Fakes.SampleScenario.SatisfactoryId);
        app.Settle();

        Assert.Equal(string.Empty, Field(app).Text);
        Assert.Equal(2, Rows(app).Count);
    }

    [AvaloniaFact]
    public async Task Writes_the_profile_search_renders_on_request()
    {
        RenderOutput.SkipUnlessRequested();
        using var app = await ComposedApp.StartWithViewAsync();
        app.Games.ProfileSearch.Text = "vanilla";
        app.Render("games", "profile-search");

        app.Games.ProfileSearch.Text = "zzz";
        app.Render("games", "profile-search-empty");
    }

    [AvaloniaFact]
    public async Task The_library_layout_has_the_same_search_over_the_table()
    {
        using var app = await ComposedApp.StartWithViewAsync(layout: Core.Catalog.ShellLayout.Library);

        Assert.True(Field(app).IsEffectivelyVisible);
        app.Press(Key.F, PhysicalKey.F, RawInputModifiers.Control);
        Assert.Same(Field(app), app.Focused);
        Assert.Equal("Search profiles", AutomationAudit.NameOf(Field(app)));
    }
}
