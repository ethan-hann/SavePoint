using Savepoint.App.Features.Common.Search;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Shell;

public class ShellGameSearchTests
{
    private static IEnumerable<string> Shown(ShellRig rig) => rig.Shell.VisibleGames.Select(row => row.Name);

    [Fact]
    public async Task With_nothing_typed_every_game_is_listed_in_order()
    {
        var rig = await ShellRig.StartAsync();

        Assert.Equal(["Skyrim Special Edition", "Satisfactory", "The Last Caretaker"], Shown(rig));
        Assert.False(rig.Shell.ShowNoGameMatches);
    }

    [Fact]
    public async Task Typing_keeps_the_games_with_every_typed_word_in_the_name_ignoring_case()
    {
        var rig = await ShellRig.StartAsync();

        rig.Shell.GameSearch.Text = "LAST care";
        Assert.Equal(["The Last Caretaker"], Shown(rig));

        rig.Shell.GameSearch.Text = "sat";
        Assert.Equal(["Satisfactory"], Shown(rig));

        rig.Shell.GameSearch.Text = "ti";
        Assert.Equal(["Skyrim Special Edition", "Satisfactory"], Shown(rig));
    }

    [Fact]
    public async Task The_initials_of_a_name_find_it_so_skyrim_se_finds_skyrim_special_edition()
    {
        var rig = await ShellRig.StartAsync();

        rig.Shell.GameSearch.Text = "skyrim se";

        Assert.Equal(["Skyrim Special Edition"], Shown(rig));
    }

    [Fact]
    public async Task Accents_in_a_name_or_in_the_search_make_no_difference()
    {
        var scenario = ScenarioBuilder.Sample().WithGame(new GameDefinition(Id: "pokemon", Name: "Pokémon Legends", Install: new InstallLocation(@"D:\Games\Pokemon")));
        var rig = await ShellRig.StartAsync(new FakeBackend(scenario));

        rig.Shell.GameSearch.Text = "pokemon";
        Assert.Equal(["Pokémon Legends"], Shown(rig));

        rig.Shell.GameSearch.Text = "POKÉMON";
        Assert.Equal(["Pokémon Legends"], Shown(rig));
    }

    [Fact]
    public async Task The_selected_game_stays_selected_while_it_still_matches()
    {
        var rig = await ShellRig.StartAsync();

        rig.Shell.GameSearch.Text = "sky";

        Assert.Equal(SampleScenario.SkyrimId, rig.Shell.SelectedGame?.Id);
        Assert.Same(rig.Shell.SelectedGameRow, rig.Shell.VisibleGames.Single());
    }

    [Fact]
    public async Task The_selected_game_stays_selected_when_the_search_hides_it_and_is_listed_again_when_it_is_cleared()
    {
        var rig = await ShellRig.StartAsync();

        rig.Shell.GameSearch.Text = "satis";

        Assert.Equal(SampleScenario.SkyrimId, rig.Shell.SelectedGame?.Id);
        Assert.Equal(3, rig.Shell.Games.Count);
        Assert.Equal(["Satisfactory"], Shown(rig));

        rig.Shell.GameSearch.ClearCommand.Execute(null);

        Assert.Equal(["Skyrim Special Edition", "Satisfactory", "The Last Caretaker"], Shown(rig));
        Assert.Equal(SampleScenario.SkyrimId, rig.Shell.SelectedGame?.Id);
    }

    [Fact]
    public async Task The_list_is_told_to_show_the_selection_again_whenever_the_search_changes_what_it_lists()
    {
        var rig = await ShellRig.StartAsync();

        var changes = rig.Changes(rig.Shell, () => rig.Shell.GameSearch.Text = "sky").ToList();

        Assert.Contains(nameof(ShellViewModel.SelectedGameRow), changes);
        Assert.Contains(nameof(ShellViewModel.ShowNoGameMatches), changes);
    }

    [Fact]
    public async Task A_search_nothing_matches_says_so_and_clearing_it_brings_the_games_back()
    {
        var rig = await ShellRig.StartAsync();

        rig.Shell.GameSearch.Text = "zzz";

        Assert.Empty(rig.Shell.VisibleGames);
        Assert.True(rig.Shell.ShowNoGameMatches);
        Assert.Equal("No games match.", rig.Shell.NoGameMatchesText);

        rig.Shell.GameSearch.ClearCommand.Execute(null);

        Assert.False(rig.Shell.ShowNoGameMatches);
        Assert.Equal(3, rig.Shell.VisibleGames.Count);
    }

    [Fact]
    public async Task An_empty_library_has_no_no_match_line()
    {
        var rig = await ShellRig.StartAsync(FakeBackend.Empty());

        rig.Shell.GameSearch.Text = "zzz";

        Assert.False(rig.Shell.ShowNoGameMatches);
    }

    [Fact]
    public async Task Opening_a_game_the_search_hides_clears_the_search_so_the_game_is_listed()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.GameSearch.Text = "sky";

        rig.Shell.SelectGame(SampleScenario.SatisfactoryId);

        Assert.Equal(string.Empty, rig.Shell.GameSearch.Text);
        Assert.Equal(3, rig.Shell.VisibleGames.Count);
        Assert.Equal(SampleScenario.SatisfactoryId, rig.Shell.SelectedGame?.Id);
    }

    [Fact]
    public async Task Opening_a_game_the_search_lists_keeps_the_search()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.GameSearch.Text = "ti";

        rig.Shell.SelectGame(SampleScenario.SatisfactoryId);

        Assert.Equal("ti", rig.Shell.GameSearch.Text);
        Assert.Equal(2, rig.Shell.VisibleGames.Count);
    }

    [Fact]
    public async Task A_library_refresh_keeps_the_search_and_applies_it_to_a_new_game()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.GameSearch.Text = "sa";

        FakeAssert.Ok(await rig.Backend.Games.AddAsync(new GameDefinition(Id: "sable", Name: "Sable", Install: new InstallLocation(@"D:\Games\Sable"))));
        FakeAssert.Ok(await rig.Backend.Games.AddAsync(new GameDefinition(Id: "hades", Name: "Hades", Install: new InstallLocation(@"D:\Games\Hades"))));
        await rig.Shell.RefreshLibraryAsync();

        Assert.Equal("sa", rig.Shell.GameSearch.Text);
        Assert.Equal(["Satisfactory", "Sable"], Shown(rig));
        Assert.Equal(5, rig.Shell.Games.Count);
    }

    [Fact]
    public async Task The_row_a_refresh_keeps_is_the_row_the_list_already_shows()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.GameSearch.Text = "sat";
        var before = rig.Shell.VisibleGames.Single();

        await rig.Shell.RefreshLibraryAsync();

        Assert.Same(before, rig.Shell.VisibleGames.Single());
    }

    [Fact]
    public async Task A_collapsed_icon_rail_has_no_search_box_so_it_lists_every_game_until_the_window_widens()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.GameSearch.Text = "sat";

        rig.Shell.SetWindowWidth(ShellViewModel.CompactBelowWidth - 1);
        Assert.Equal(3, rig.Shell.VisibleGames.Count);
        Assert.False(rig.Shell.ShowRailSearch);

        rig.Shell.SetWindowWidth(ShellViewModel.CompactBelowWidth);
        Assert.Equal(["Satisfactory"], Shown(rig));
        Assert.True(rig.Shell.ShowRailSearch);
    }

    [Fact]
    public async Task The_library_shelf_keeps_its_search_in_a_narrow_window()
    {
        var rig = await ShellRig.LibraryLayoutAsync();
        rig.Shell.GameSearch.Text = "sat";

        rig.Shell.SetWindowWidth(ShellViewModel.CompactBelowWidth - 1);

        Assert.Equal(["Satisfactory"], Shown(rig));
    }

    [Fact]
    public async Task Switching_layout_in_a_narrow_window_applies_the_search_where_the_box_shows_and_ignores_it_where_it_does_not()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.GameSearch.Text = "sat";
        rig.Shell.SetWindowWidth(ShellViewModel.CompactBelowWidth - 1);
        Assert.Equal(3, rig.Shell.VisibleGames.Count);

        await rig.Shell.SetLayoutAsync(ShellLayout.Library);
        Assert.Equal(["Satisfactory"], Shown(rig));

        await rig.Shell.SetLayoutAsync(ShellLayout.Sidebar);
        Assert.Equal(3, rig.Shell.VisibleGames.Count);
    }

    [Fact]
    public async Task The_count_left_is_announced_once_typing_pauses()
    {
        var clock = new ManualTimeProvider();
        var rig = await ShellRig.StartAsync(clock: clock);

        rig.Shell.GameSearch.Text = "s";
        rig.Shell.GameSearch.Text = "sa";
        Assert.Equal(string.Empty, rig.Shell.GameSearch.Announcement);
        clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Equal("1 of 3 games", rig.Shell.GameSearch.Announcement);
    }

    [Fact]
    public async Task One_game_in_the_library_reads_as_one_game()
    {
        var scenario = ScenarioBuilder.Empty().WithGame(new GameDefinition(Id: "sable", Name: "Sable", Install: new InstallLocation(@"D:\Games\Sable")));
        var clock = new ManualTimeProvider();
        var rig = await ShellRig.StartAsync(new FakeBackend(scenario), clock: clock);

        rig.Shell.GameSearch.Text = "sab";
        clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Equal("1 of 1 game", rig.Shell.GameSearch.Announcement);
    }

    [Fact]
    public async Task Removing_the_selected_game_selects_the_first_game_the_search_lists()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.GameSearch.Text = "last";

        rig.Backend.State.Games.RemoveAll(game => game.Id == SampleScenario.SkyrimId);
        await rig.Shell.RefreshLibraryAsync();

        Assert.Equal(["The Last Caretaker"], Shown(rig));
        Assert.Equal(rig.Shell.VisibleGames[0], rig.Shell.SelectedGameRow);
    }

    [Fact]
    public async Task Removing_the_selected_game_with_nothing_typed_selects_the_first_game()
    {
        var rig = await ShellRig.StartAsync();

        rig.Backend.State.Games.RemoveAll(game => game.Id == SampleScenario.SkyrimId);
        await rig.Shell.RefreshLibraryAsync();

        Assert.Equal(SampleScenario.SatisfactoryId, rig.Shell.SelectedGame?.Id);
    }

    [Fact]
    public async Task A_refresh_alone_announces_nothing()
    {
        var clock = new ManualTimeProvider();
        var rig = await ShellRig.StartAsync(clock: clock);

        await rig.Shell.RefreshLibraryAsync();
        clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Equal(string.Empty, rig.Shell.GameSearch.Announcement);
    }

    [Fact]
    public async Task Hundreds_of_games_are_filtered_by_name_and_keep_their_order()
    {
        var scenario = ScenarioBuilder.Empty();
        for (var i = 0; i < 600; i++)
        {
            scenario.WithGame(new GameDefinition(Id: $"game-{i}", Name: $"Game {i:000} {(i % 50 == 0 ? "Deluxe" : "Standard")} Edition", Install: new InstallLocation($@"D:\Games\{i}")));
        }

        var rig = await ShellRig.StartAsync(new FakeBackend(scenario));
        rig.Shell.GameSearch.Text = "deluxe ed";

        Assert.Equal(12, rig.Shell.VisibleGames.Count);
        Assert.Equal("Game 000 Deluxe Edition", rig.Shell.VisibleGames[0].Name);
        Assert.Equal("Game 550 Deluxe Edition", rig.Shell.VisibleGames[^1].Name);
        Assert.Equal(600, rig.Shell.Games.Count);
    }
}
