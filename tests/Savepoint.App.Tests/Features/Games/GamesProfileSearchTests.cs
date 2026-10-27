using Savepoint.App.Features.Common.Search;
using Savepoint.App.Features.Games;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

public class GamesProfileSearchTests
{
    private const string _modded = "Modded playthrough - 7-2-26";
    private const string _vanilla = "Vanilla - latest update";
    private const string _survival = "Survival test - 5-14-26";

    private readonly ManualTimeProvider _clock = new();

    private static FakeBackend Library()
    {
        var scenario = ScenarioBuilder.Sample()
            .WithProfile(new Profile("hardcore", SampleScenario.SkyrimId, "Hardcore run", "profile/hardcore", Notes: "No fast travel, vampire build"))
            .WithProfile(new Profile("cafe", SampleScenario.SkyrimId, "Café mod test", "profile/cafe"));
        return new FakeBackend(scenario);
    }

    private async Task<GamesRig> StartAsync() => await GamesRig.StartAsync(Library(), clock: _clock);

    private static IEnumerable<string> Shown(GamesRig rig) => rig.Screen.VisibleProfiles.Select(row => row.Name);

    [Fact]
    public async Task With_nothing_typed_every_profile_is_listed_in_table_order()
    {
        var rig = await StartAsync();

        Assert.Equal(["Modded playthrough - 7-2-26", "Vanilla - latest update", "Survival test - 5-14-26", "Hardcore run", "Café mod test"], Shown(rig));
        Assert.True(rig.Screen.ShowSearch);
        Assert.True(rig.Screen.ShowRows);
        Assert.False(rig.Screen.ShowNoProfileMatches);
    }

    [Fact]
    public async Task Typing_keeps_the_profiles_whose_name_has_every_word_ignoring_case_and_order()
    {
        var rig = await StartAsync();

        rig.Screen.ProfileSearch.Text = "TEST survival";
        Assert.Equal([_survival], Shown(rig));

        rig.Screen.ProfileSearch.Text = "5-14";
        Assert.Equal([_survival], Shown(rig));

        rig.Screen.ProfileSearch.Text = "mod";
        Assert.Equal([_modded, "Café mod test"], Shown(rig));
    }

    [Fact]
    public async Task Typing_also_looks_in_the_profiles_notes()
    {
        var rig = await StartAsync();

        rig.Screen.ProfileSearch.Text = "vampire";
        Assert.Equal(["Hardcore run"], Shown(rig));

        rig.Screen.ProfileSearch.Text = "hardcore travel";
        Assert.Equal(["Hardcore run"], Shown(rig));
    }

    [Fact]
    public async Task Accents_make_no_difference_either_way()
    {
        var rig = await StartAsync();

        rig.Screen.ProfileSearch.Text = "cafe";
        Assert.Equal(["Café mod test"], Shown(rig));

        rig.Screen.ProfileSearch.Text = "CAFÉ";
        Assert.Equal(["Café mod test"], Shown(rig));
    }

    [Fact]
    public async Task The_full_list_is_kept_for_what_the_screen_does_with_every_profile()
    {
        var rig = await StartAsync();

        rig.Screen.ProfileSearch.Text = "vampire";

        Assert.Equal(5, rig.Screen.Profiles.Count);
        Assert.True(rig.Screen.HasLoadedProfile);
    }

    [Fact]
    public async Task A_search_nothing_matches_says_so_and_keeps_the_search_box_for_clearing_it()
    {
        var rig = await StartAsync();

        rig.Screen.ProfileSearch.Text = "zzz";

        Assert.Empty(rig.Screen.VisibleProfiles);
        Assert.True(rig.Screen.ShowSearch);
        Assert.True(rig.Screen.ShowNoProfileMatches);
        Assert.False(rig.Screen.ShowRows);
        Assert.Equal("No profiles match.", rig.Screen.NoProfileMatchesText);

        rig.Screen.ProfileSearch.ClearCommand.Execute(null);

        Assert.False(rig.Screen.ShowNoProfileMatches);
        Assert.True(rig.Screen.ShowRows);
        Assert.Equal(5, rig.Screen.VisibleProfiles.Count);
    }

    [Fact]
    public async Task While_a_search_narrows_the_table_a_line_says_how_many_profiles_it_shows()
    {
        var rig = await StartAsync();
        Assert.False(rig.Screen.ShowProfileCount);

        rig.Screen.ProfileSearch.Text = "mod";

        Assert.True(rig.Screen.ShowProfileCount);
        Assert.Equal("2 of 5 profiles", rig.Screen.ProfileCountText);

        rig.Screen.ProfileSearch.Text = "vanilla";
        Assert.Equal("1 of 5 profiles", rig.Screen.ProfileCountText);

        rig.Screen.ProfileSearch.ClearCommand.Execute(null);
        Assert.False(rig.Screen.ShowProfileCount);
    }

    [Fact]
    public async Task The_count_line_gives_way_to_the_no_match_line_when_nothing_matches()
    {
        var rig = await StartAsync();

        rig.Screen.ProfileSearch.Text = "zzz";

        Assert.False(rig.Screen.ShowProfileCount);
    }

    [Fact]
    public async Task The_count_line_follows_the_search_as_it_changes()
    {
        var rig = await StartAsync();

        List<string?> changes = [];
        rig.Screen.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        rig.Screen.ProfileSearch.Text = "mod";

        Assert.Contains(nameof(GamesScreenViewModel.ProfileCountText), changes);
        Assert.Contains(nameof(GamesScreenViewModel.ShowProfileCount), changes);
    }

    [Fact]
    public async Task A_game_with_no_profiles_has_no_search_box()
    {
        var scenario = ScenarioBuilder.Empty().WithSettings(SampleScenario.Settings).WithGame(SampleScenario.Caretaker);
        var rig = await GamesRig.StartAsync(new FakeBackend(scenario), clock: _clock);

        Assert.False(rig.Screen.ShowSearch);
        Assert.False(rig.Screen.ShowNoProfileMatches);
    }

    [Fact]
    public async Task The_search_is_kept_when_the_screen_reads_the_same_game_again_and_the_rows_stay_the_same_objects()
    {
        var rig = await StartAsync();
        rig.Screen.ProfileSearch.Text = "vanilla";
        var before = rig.Screen.VisibleProfiles.Single();

        rig.Screen.OnShown();
        await rig.Screen.ReloadAsync();

        Assert.Equal("vanilla", rig.Screen.ProfileSearch.Text);
        Assert.Same(before, rig.Screen.VisibleProfiles.Single());
    }

    [Fact]
    public async Task A_duplicate_the_search_would_hide_clears_the_search_so_it_shows()
    {
        var rig = await StartAsync();
        rig.Screen.ProfileSearch.Text = "modded";

        var shown = rig.Screen.VisibleProfiles.Single().DuplicateCommand.ExecuteAsync(null);
        var dialog = Assert.IsType<DuplicateProfileDialogViewModel>(Assert.Single(rig.Dialogs.Shown));
        dialog.Name = "Tidy copy";
        await dialog.SaveCommand.ExecuteAsync(null);
        await shown.Soon();

        Assert.Equal(string.Empty, rig.Screen.ProfileSearch.Text);
        Assert.Contains("Tidy copy", Shown(rig));
        Assert.Equal(6, rig.Screen.VisibleProfiles.Count);
    }

    [Fact]
    public async Task A_profile_made_with_the_wizard_that_the_search_would_hide_clears_the_search_so_it_shows()
    {
        var rig = await StartAsync();
        rig.Screen.ProfileSearch.Text = "modded";

        var shown = rig.Screen.NewProfileCommand.ExecuteAsync(null);
        var wizard = Assert.IsType<NewProfileWizardViewModel>(Assert.Single(rig.Dialogs.Shown));
        wizard.Name = "Vanilla";
        await wizard.ContinueCommand.ExecuteAsync(null);
        await wizard.ContinueCommand.ExecuteAsync(null);
        rig.Backend.Jobs.Complete(rig.Backend.Jobs.LastStarted(JobKind.Capture).JobId);
        wizard.CloseCommand.Execute(null);
        await shown.Soon();

        Assert.Equal(string.Empty, rig.Screen.ProfileSearch.Text);
        Assert.Contains("Vanilla", Shown(rig));
    }

    [Fact]
    public async Task A_profile_brought_back_from_history_that_the_search_would_hide_clears_the_search_so_it_shows()
    {
        var scenario = ScenarioBuilder.Sample().WithRemovedSnapshot(SampleScenario.SkyrimId, RemovedSnapshotSamples.Continued);
        var rig = await GamesRig.StartAsync(new FakeBackend(scenario), clock: _clock);
        rig.Screen.ProfileSearch.Text = "modded";

        await rig.Backend.Profiles.BringBackAsync(SampleScenario.SkyrimId, RemovedSnapshotSamples.Continued.Id, "Boss fight again");
        await rig.Screen.ReloadAsync();

        Assert.Equal(string.Empty, rig.Screen.ProfileSearch.Text);
        Assert.Contains("Boss fight again", Shown(rig));
    }

    [Fact]
    public async Task A_reload_that_adds_nothing_keeps_the_search()
    {
        var rig = await StartAsync();
        rig.Screen.ProfileSearch.Text = "modded";

        await rig.Screen.ReloadAsync();

        Assert.Equal("modded", rig.Screen.ProfileSearch.Text);
        Assert.Equal([_modded], Shown(rig));
    }

    [Fact]
    public async Task Choosing_another_game_clears_the_search_and_lists_all_its_profiles()
    {
        var rig = await StartAsync();
        rig.Screen.ProfileSearch.Text = "vanilla";

        rig.Shell.SelectGame(SampleScenario.SatisfactoryId);
        await rig.Screen.ReloadAsync();

        Assert.Equal(string.Empty, rig.Screen.ProfileSearch.Text);
        Assert.Equal(["Main factory - 6-1-26", "Update 1.1 test - 6-20-26"], Shown(rig));
    }

    [Fact]
    public async Task Clearing_for_another_game_announces_nothing()
    {
        var rig = await StartAsync();
        rig.Screen.ProfileSearch.Text = "vanilla";
        _clock.Advance(SearchBoxViewModel.AnnouncePause);
        var said = rig.Screen.ProfileSearch.Announcement;

        rig.Shell.SelectGame(SampleScenario.SatisfactoryId);
        await rig.Screen.ReloadAsync();
        _clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Equal("1 of 5 profiles", said);
        Assert.Equal(said, rig.Screen.ProfileSearch.Announcement);
    }

    [Fact]
    public async Task The_count_left_is_announced_once_typing_pauses()
    {
        var rig = await StartAsync();

        rig.Screen.ProfileSearch.Text = "m";
        rig.Screen.ProfileSearch.Text = "mod";
        Assert.Equal(string.Empty, rig.Screen.ProfileSearch.Announcement);
        _clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Equal("2 of 5 profiles", rig.Screen.ProfileSearch.Announcement);
    }

    [Fact]
    public async Task Hundreds_of_profiles_are_filtered_by_name_and_notes()
    {
        var scenario = ScenarioBuilder.Sample();
        for (var i = 0; i < 400; i++)
        {
            scenario.WithProfile(new Profile($"p{i}", SampleScenario.SkyrimId, $"Run {i:000}", $"profile/p{i}", Notes: i % 40 == 0 ? "boss rush" : null));
        }

        var rig = await GamesRig.StartAsync(new FakeBackend(scenario), clock: _clock);
        rig.Screen.ProfileSearch.Text = "rush run";

        Assert.Equal(10, rig.Screen.VisibleProfiles.Count);
        Assert.Equal("Run 000", rig.Screen.VisibleProfiles[0].Name);
        Assert.Equal("Run 360", rig.Screen.VisibleProfiles[^1].Name);
    }
}
