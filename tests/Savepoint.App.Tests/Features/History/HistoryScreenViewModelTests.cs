using Savepoint.App.Features.History;
using Savepoint.App.Features.Shell;
using Savepoint.App.Theme;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.History;

public class HistoryScreenViewModelTests
{
    private const string _modded = "Modded playthrough - 7-2-26";

    private static FakeBackend WithEmptyProfile() => new(ScenarioBuilder.Mockup()
        .WithProfile(new Profile("profile-empty", MockupScenario.SkyrimId, "Fresh profile", "profile/profile-empty")));

    [Fact]
    public async Task Showing_the_screen_reads_the_selected_games_history()
    {
        var rig = await HistoryRig.StartAsync();

        rig.Show();

        var read = Assert.Single(rig.Backend.Calls.To<IHistoryApi>(nameof(IHistoryApi.GetAsync)));
        Assert.Equal([MockupScenario.SkyrimId, null], read.Args);
        Assert.Equal(HistoryState.Loaded, rig.Screen.State);
    }

    [Fact]
    public async Task Header_names_the_game_and_counts_capture_events_not_column_rows()
    {
        var rig = await HistoryRig.ShownAsync();

        Assert.Equal("Skyrim Special Edition", rig.Screen.GameName);
        Assert.Equal("Skyrim Special Edition · 8 points kept", rig.Screen.Subtitle);
    }

    [Fact]
    public async Task Versions_and_mods_column_lists_the_points_that_changed_the_install_newest_first()
    {
        var rig = await HistoryRig.ShownAsync();

        Assert.Equal(
            [
                "14 mod files updated",
                "Captured 1.6.640 with 198 mod files",
                "Captured 1.6.1170, no mods",
                "Captured 1.6.640 with 189 mod files",
            ],
            rig.Screen.Timeline.Select(entry => entry.Headline));
    }

    [Fact]
    public async Task Timeline_details_name_the_profile_and_add_the_version_or_size_the_headline_does_not_give()
    {
        var rig = await HistoryRig.ShownAsync();

        Assert.Equal(
            [
                $"{_modded} · version 1.6.640",
                $"{_modded} · 41.2 GB",
                "Vanilla - latest update",
                "Survival test - 5-14-26",
            ],
            rig.Screen.Timeline.Select(entry => entry.Detail));
    }

    [Fact]
    public async Task Timeline_stamps_read_the_capture_time_and_mark_the_point_in_use()
    {
        var rig = await HistoryRig.ShownAsync();

        Assert.Equal(
            [
                "Jul 2, 2026 · 6:03 PM · in use now",
                "Jul 2, 2026 · 5:47 PM",
                "Jun 21, 2026 · 9:12 PM",
                "May 14, 2026 · 8:30 PM",
            ],
            rig.Screen.Timeline.Select(entry => entry.Stamp));
        Assert.Equal([true, false, false, false], rig.Screen.Timeline.Select(entry => entry.IsInUse));
    }

    [Fact]
    public async Task The_rail_runs_to_the_oldest_entry_and_stops_there()
    {
        var rig = await HistoryRig.ShownAsync();

        Assert.Equal([true, true, true, false], rig.Screen.Timeline.Select(entry => entry.HasRail));
    }

    [Fact]
    public async Task Saves_column_lists_save_points_newest_first_and_a_version_mods_and_save_point_appears_in_both_columns()
    {
        var rig = await HistoryRig.ShownAsync();

        Assert.Equal(
            ["Before the final boss", "Cleared Blackreach", "Fresh start with Nolvus", "Deep cave run", "First night"],
            rig.Screen.Saves.Select(row => row.Name));
        Assert.Contains(rig.Screen.Timeline, entry => entry.Headline == "Captured 1.6.640 with 198 mod files");
    }

    [Fact]
    public async Task Save_rows_give_the_date_and_the_owning_profile_and_mark_the_loaded_save()
    {
        var rig = await HistoryRig.ShownAsync();

        Assert.Equal($"Jul 5, 2026, 11:42 PM · {_modded}", rig.Screen.Saves[0].Detail);
        Assert.Equal("May 30, 2026, 11:42 PM · Survival test - 5-14-26", rig.Screen.Saves[3].Detail);
        Assert.Equal([true, false, false, false, false], rig.Screen.Saves.Select(row => row.IsLoaded));
        Assert.Equal([false, true, true, true, true], rig.Screen.Saves.Select(row => row.CanLoad));
    }

    [Fact]
    public async Task Cards_group_the_points_into_this_week_and_earlier_by_the_clock()
    {
        var rig = await HistoryRig.ShownAsync();

        Assert.Equal(["This week", "Earlier"], rig.Screen.Groups.Select(group => group.Heading));
        Assert.Equal(
            ["Before the final boss", "Cleared Blackreach", "14 mod files updated", "Fresh start with Nolvus"],
            rig.Screen.Groups[0].Cards.Select(card => card.Name));
        Assert.Equal(
            ["Vanilla 1.6.1170", "Deep cave run", "First night", "Survival test - 5-14-26"],
            rig.Screen.Groups[1].Cards.Select(card => card.Name));
    }

    [Fact]
    public async Task A_group_with_no_cards_is_left_out()
    {
        var rig = await HistoryRig.StartAsync();
        rig.Backend.Clock.Advance(TimeSpan.FromDays(60));

        rig.Show();

        Assert.Equal(["Earlier"], rig.Screen.Groups.Select(group => group.Heading));
    }

    [Fact]
    public async Task A_card_reads_what_the_point_holds_from_the_install_state_at_that_point()
    {
        var rig = await HistoryRig.ShownAsync();
        var cards = rig.Screen.Groups.SelectMany(group => group.Cards).ToDictionary(card => card.Name);

        Assert.Equal("Save only · 1.6.640 with 212 mod files", cards["Before the final boss"].Contains);
        Assert.Equal("Version and mods · 1.6.640 with 212 mod files", cards["14 mod files updated"].Contains);
        Assert.Equal("Version and mods · 1.6.1170, no mods", cards["Vanilla 1.6.1170"].Contains);
        Assert.Equal("Version, mods and save · 1.6.640 with 198 mod files", cards["Fresh start with Nolvus"].Contains);
    }

    [Fact]
    public async Task A_card_has_the_icon_for_its_kind_and_the_absolute_date()
    {
        var rig = await HistoryRig.ShownAsync();
        var cards = rig.Screen.Groups.SelectMany(group => group.Cards).ToDictionary(card => card.Name);

        Assert.Equal(Icons.Save, cards["Cleared Blackreach"].Icon);
        Assert.Equal(Icons.Extension, cards["14 mod files updated"].Icon);
        Assert.Equal(Icons.BookmarkAdd, cards["Fresh start with Nolvus"].Icon);
        Assert.Equal("Jul 4, 2026, 8:15 PM", cards["Cleared Blackreach"].Date);
    }

    [Fact]
    public async Task The_loaded_card_is_marked_and_offers_no_button()
    {
        var rig = await HistoryRig.ShownAsync();
        var cards = rig.Screen.Groups.SelectMany(group => group.Cards).ToDictionary(card => card.Name);

        Assert.True(cards["Before the final boss"].IsLoaded);
        Assert.False(cards["Before the final boss"].CanGoBack);
        Assert.False(cards["Cleared Blackreach"].IsLoaded);
        Assert.True(cards["Cleared Blackreach"].CanGoBack);
    }

    [Fact]
    public async Task Go_back_to_this_begins_the_launch_flow_at_that_point()
    {
        var rig = await HistoryRig.ShownAsync();
        var card = rig.Screen.Groups.SelectMany(group => group.Cards).Single(c => c.Name == "Cleared Blackreach");

        card.GoBackCommand.Execute(null);

        var begun = Assert.Single(rig.Flow!.Begun);
        Assert.Equal(MockupScenario.SkyrimId, begun.GameId);
        Assert.Equal(new LaunchTarget.Point(MockupScenario.ClearedBlackreach), begun.Target);
    }

    [Fact]
    public async Task Load_begins_the_launch_flow_with_that_save_in_the_points_own_profile()
    {
        var rig = await HistoryRig.ShownAsync();
        var row = rig.Screen.Saves.Single(r => r.Name == "Deep cave run");

        row.LoadCommand.Execute(null);

        var begun = Assert.Single(rig.Flow!.Begun);
        Assert.Equal(MockupScenario.SkyrimId, begun.GameId);
        Assert.Equal(new LaunchTarget.Save(MockupScenario.SurvivalId, MockupScenario.DeepCaveRun), begun.Target);
    }

    [Fact]
    public async Task Buttons_have_names_that_tell_the_points_apart()
    {
        var rig = await HistoryRig.ShownAsync();

        Assert.Equal("Load Cleared Blackreach", rig.Screen.Saves.Single(r => r.Name == "Cleared Blackreach").LoadAutomationName);
        Assert.Equal(
            "Go back to Cleared Blackreach",
            rig.Screen.Groups.SelectMany(group => group.Cards).Single(c => c.Name == "Cleared Blackreach").GoBackAutomationName);
    }

    [Fact]
    public async Task Buttons_are_unavailable_when_no_launch_flow_is_plugged_in()
    {
        var rig = await HistoryRig.ShownAsync(withFlow: false);

        Assert.False(rig.Screen.Saves[1].LoadCommand.CanExecute(null));
        Assert.False(rig.Screen.Groups[0].Cards[1].GoBackCommand.CanExecute(null));
    }

    [Fact]
    public async Task Buttons_are_unavailable_while_the_flow_holds_a_session_for_the_game_and_return_when_it_ends()
    {
        var rig = await HistoryRig.ShownAsync();
        var load = rig.Screen.Saves[1].LoadCommand;
        var goBack = rig.Screen.Groups[0].Cards[1].GoBackCommand;
        Assert.True(load.CanExecute(null));
        Assert.True(goBack.CanExecute(null));
        var changes = 0;
        load.CanExecuteChanged += (_, _) => changes++;

        rig.Flow!.SetActive(MockupScenario.SkyrimId, true);

        Assert.False(load.CanExecute(null));
        Assert.False(goBack.CanExecute(null));
        Assert.True(changes > 0);

        rig.Flow.SetActive(MockupScenario.SkyrimId, false);

        Assert.True(load.CanExecute(null));
        Assert.True(goBack.CanExecute(null));
    }

    [Fact]
    public async Task A_session_for_another_game_does_not_disable_the_buttons()
    {
        var rig = await HistoryRig.ShownAsync();

        rig.Flow!.SetActive(MockupScenario.SatisfactoryId, true);

        Assert.True(rig.Screen.Saves[1].LoadCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_filter_lists_every_profile_after_all_profiles()
    {
        var rig = await HistoryRig.ShownAsync();

        Assert.Equal(
            ["All profiles", _modded, "Vanilla - latest update", "Survival test - 5-14-26"],
            rig.Screen.Filters.Select(filter => filter.Label));
        Assert.Null(rig.Screen.SelectedFilter!.ProfileId);
    }

    [Fact]
    public async Task Choosing_a_profile_reads_only_its_points_and_recounts_them()
    {
        var rig = await HistoryRig.ShownAsync();

        rig.Screen.SelectedFilter = rig.Screen.Filters.Single(filter => filter.ProfileId == MockupScenario.ModdedId);

        var read = rig.Backend.Calls.To<IHistoryApi>(nameof(IHistoryApi.GetAsync)).Last();
        Assert.Equal([MockupScenario.SkyrimId, MockupScenario.ModdedId], read.Args);
        Assert.Equal("Skyrim Special Edition · 4 points kept", rig.Screen.Subtitle);
        Assert.Equal(["14 mod files updated", "Captured 1.6.640 with 198 mod files"], rig.Screen.Timeline.Select(entry => entry.Headline));
    }

    [Fact]
    public async Task One_point_reads_in_the_singular()
    {
        var rig = await HistoryRig.ShownAsync();

        rig.Screen.SelectedFilter = rig.Screen.Filters.Single(filter => filter.ProfileId == MockupScenario.VanillaId);

        Assert.Equal("Skyrim Special Edition · 1 point kept", rig.Screen.Subtitle);
    }

    [Fact]
    public async Task Choosing_all_profiles_again_brings_the_whole_history_back()
    {
        var rig = await HistoryRig.ShownAsync();
        rig.Screen.SelectedFilter = rig.Screen.Filters[1];

        rig.Screen.SelectedFilter = rig.Screen.Filters[0];

        Assert.Equal("Skyrim Special Edition · 8 points kept", rig.Screen.Subtitle);
        Assert.Null(rig.Backend.Calls.To<IHistoryApi>(nameof(IHistoryApi.GetAsync)).Last().Args[1]);
    }

    [Fact]
    public async Task Choosing_the_filter_that_is_already_chosen_reads_nothing()
    {
        var rig = await HistoryRig.ShownAsync();
        var reads = rig.HistoryReads;

        rig.Screen.SelectedFilter = rig.Screen.Filters[0];

        Assert.Equal(reads, rig.HistoryReads);
    }

    [Fact]
    public async Task A_profile_with_no_points_shows_the_empty_state_and_show_all_clears_the_filter()
    {
        var rig = await HistoryRig.ShownAsync(WithEmptyProfile());

        rig.Screen.SelectedFilter = rig.Screen.Filters.Single(filter => filter.ProfileId == "profile-empty");

        Assert.Equal(HistoryState.Empty, rig.Screen.State);
        Assert.True(rig.Screen.IsFilteredEmpty);

        rig.Screen.ShowAllProfilesCommand.Execute(null);

        Assert.Equal(HistoryState.Loaded, rig.Screen.State);
        Assert.False(rig.Screen.IsFilteredEmpty);
    }

    [Fact]
    public async Task A_game_with_no_points_at_all_shows_the_plain_empty_state()
    {
        var rig = await HistoryRig.StartAsync();
        rig.Shell.SelectGame(MockupScenario.SatisfactoryId);

        rig.Show();

        Assert.Equal(HistoryState.Empty, rig.Screen.State);
        Assert.False(rig.Screen.IsFilteredEmpty);
        Assert.Equal("Satisfactory · 0 points kept", rig.Screen.Subtitle);
    }

    [Fact]
    public async Task An_empty_library_shows_the_no_game_state()
    {
        var rig = await HistoryRig.StartAsync(FakeBackend.Empty());

        rig.Show();

        Assert.Equal(HistoryState.NoGame, rig.Screen.State);
        Assert.Null(rig.Screen.GameName);
        Assert.Equal(0, rig.HistoryReads);
    }

    [Fact]
    public async Task Cards_are_the_default_view_and_the_choice_is_remembered_for_the_session()
    {
        var rig = await HistoryRig.ShownAsync();
        Assert.Equal(HistoryViewMode.Cards, rig.Screen.ViewMode);
        Assert.True(rig.Screen.IsCardsView);
        Assert.False(rig.Screen.IsListView);

        rig.Screen.SelectedViewOption = rig.Screen.ViewOptions.Single(option => option.Mode == HistoryViewMode.List);
        rig.Shell.Navigate(NavDestination.Games);
        rig.Shell.SelectGame(MockupScenario.SatisfactoryId);
        rig.Show();

        Assert.Equal(HistoryViewMode.List, rig.Screen.ViewMode);
        Assert.True(rig.Screen.IsListView);
        Assert.False(rig.Screen.IsCardsView);
    }

    [Fact]
    public async Task Choosing_a_view_reads_nothing()
    {
        var rig = await HistoryRig.ShownAsync();
        var reads = rig.HistoryReads;

        rig.Screen.SelectedViewOption = rig.Screen.ViewOptions[1];

        Assert.Equal(reads, rig.HistoryReads);
    }

    [Fact]
    public async Task A_list_box_clearing_the_view_choice_while_it_rebuilds_changes_nothing()
    {
        var rig = await HistoryRig.ShownAsync();

        rig.Screen.SelectedViewOption = null;

        Assert.Equal(HistoryViewMode.Cards, rig.Screen.ViewMode);
        Assert.NotNull(rig.Screen.SelectedViewOption);
    }

    [Fact]
    public async Task The_first_read_shows_loading_until_it_returns()
    {
        GatedHistoryApi? gate = null;
        var rig = await HistoryRig.StartAsync(history: inner => gate = new GatedHistoryApi(inner) { Hold = true });

        rig.Show();

        Assert.Equal(HistoryState.Loading, rig.Screen.State);
        Assert.Empty(rig.Screen.Saves);

        gate!.Pending.Single().Release();

        Assert.Equal(HistoryState.Loaded, rig.Screen.State);
        Assert.Equal(5, rig.Screen.Saves.Count);
    }

    [Fact]
    public async Task Reading_the_same_game_again_keeps_the_content_instead_of_flashing_loading()
    {
        GatedHistoryApi? gate = null;
        var rig = await HistoryRig.StartAsync(history: inner => gate = new GatedHistoryApi(inner));
        rig.Show();

        gate!.Hold = true;
        var reload = rig.Screen.ReloadAsync();

        Assert.False(reload.IsCompleted);
        Assert.Equal(HistoryState.Loaded, rig.Screen.State);
        Assert.Equal(5, rig.Screen.Saves.Count);

        gate.Pending.Single().Release();
        await reload;

        Assert.Equal(HistoryState.Loaded, rig.Screen.State);
    }

    [Fact]
    public async Task A_failed_read_shows_an_error_card_and_try_again_reads_again()
    {
        var rig = await HistoryRig.StartAsync();
        var error = ErrorInfo.From(new InvalidOperationException("The history could not be read."));
        rig.Backend.FailNext<IHistoryApi>(nameof(IHistoryApi.GetAsync), error);

        rig.Show();

        Assert.Equal(HistoryState.Failed, rig.Screen.State);
        Assert.NotNull(rig.Screen.ErrorCard);
        var retry = Assert.Single(rig.Screen.ErrorCard.Actions);
        Assert.Equal("Try again", retry.Label);

        retry.Command.Execute(null);

        Assert.Equal(HistoryState.Loaded, rig.Screen.State);
        Assert.Null(rig.Screen.ErrorCard);
    }

    [Fact]
    public async Task The_answer_to_a_read_that_was_superseded_is_dropped_even_when_it_arrives_last()
    {
        GatedHistoryApi? gate = null;
        var rig = await HistoryRig.StartAsync(history: inner => gate = new GatedHistoryApi(inner));
        rig.Show();
        gate!.Hold = true;

        rig.Screen.SelectedFilter = rig.Screen.Filters.Single(filter => filter.ProfileId == MockupScenario.ModdedId);
        rig.Screen.SelectedFilter = rig.Screen.Filters.Single(filter => filter.ProfileId == MockupScenario.VanillaId);
        Assert.Equal(2, gate.Pending.Count);

        gate.Pending[1].Release();
        gate.Pending[0].Release();

        Assert.Equal("Skyrim Special Edition · 1 point kept", rig.Screen.Subtitle);
        Assert.Equal(["Captured 1.6.1170, no mods"], rig.Screen.Timeline.Select(entry => entry.Headline));
    }

    [Fact]
    public async Task A_read_cancelled_by_a_newer_one_never_shows_an_error()
    {
        GatedHistoryApi? gate = null;
        var rig = await HistoryRig.StartAsync(history: inner => gate = new GatedHistoryApi(inner));
        rig.Show();
        gate!.Hold = true;
        rig.Backend.FailNext<IHistoryApi>(nameof(IHistoryApi.GetAsync), ErrorInfo.From(new OperationCancelledByUserException(CancelledOperation.Capture, null)));

        rig.Screen.SelectedFilter = rig.Screen.Filters[1];
        rig.Screen.SelectedFilter = rig.Screen.Filters[2];

        // The newer answer lands first and the cancelled one last, so only the superseded-read guard keeps its error off the screen.
        foreach (var pending in gate.Pending.ToList().AsEnumerable().Reverse())
        {
            pending.Release();
        }

        Assert.NotEqual(HistoryState.Failed, rig.Screen.State);
        Assert.Null(rig.Screen.ErrorCard);
    }

    [Fact]
    public async Task Selecting_another_game_while_the_screen_shows_reads_its_history_and_resets_the_filter()
    {
        var rig = await HistoryRig.ShownAsync();
        rig.Screen.SelectedFilter = rig.Screen.Filters[1];

        rig.Shell.SelectGame(MockupScenario.SatisfactoryId);

        var read = rig.Backend.Calls.To<IHistoryApi>(nameof(IHistoryApi.GetAsync)).Last();
        Assert.Equal([MockupScenario.SatisfactoryId, null], read.Args);
        Assert.Equal("Satisfactory", rig.Screen.GameName);
        Assert.Null(rig.Screen.SelectedFilter!.ProfileId);
    }

    [Fact]
    public async Task A_library_refresh_while_the_screen_shows_reads_again()
    {
        var rig = await HistoryRig.ShownAsync();
        var reads = rig.HistoryReads;

        await rig.Shell.RefreshLibraryAsync();

        Assert.Equal(reads + 1, rig.HistoryReads);
    }

    [Fact]
    public async Task A_finished_capture_while_the_screen_shows_reads_again_so_the_markers_stay_current()
    {
        var rig = await HistoryRig.ShownAsync();
        var capture = FakeAssert.Ok(await rig.Backend.Profiles.CaptureAsync(MockupScenario.SkyrimId, MockupScenario.ModdedId, "A point"));
        var reads = rig.HistoryReads;

        rig.Backend.Jobs.Complete(capture.JobId);

        Assert.True(rig.HistoryReads > reads);
    }

    [Fact]
    public async Task A_job_that_is_still_running_does_not_trigger_a_read()
    {
        var rig = await HistoryRig.ShownAsync();
        var reads = rig.HistoryReads;

        FakeAssert.Ok(await rig.Backend.Profiles.CaptureAsync(MockupScenario.SkyrimId, MockupScenario.ModdedId, "A point"));

        Assert.Equal(reads, rig.HistoryReads);
    }

    [Fact]
    public async Task Nothing_is_read_while_another_screen_shows_and_coming_back_reads_the_current_game()
    {
        var rig = await HistoryRig.ShownAsync();
        rig.Shell.Navigate(NavDestination.Games);
        var reads = rig.HistoryReads;

        rig.Shell.SelectGame(MockupScenario.SatisfactoryId);
        await rig.Shell.RefreshLibraryAsync();

        Assert.Equal(reads, rig.HistoryReads);

        rig.Show();

        Assert.Equal(reads + 1, rig.HistoryReads);
        Assert.Equal("Satisfactory", rig.Screen.GameName);
    }

    [Fact]
    public async Task The_footer_states_the_space_kept_for_the_game()
    {
        var rig = await HistoryRig.ShownAsync();

        Assert.Equal(
            "41.2 GB kept for this game. Deleting a profile frees its space the next time storage is optimized.",
            rig.Screen.FooterText);
        Assert.True(rig.Screen.HasFooter);
    }

    [Fact]
    public async Task The_footer_is_left_out_when_the_size_cannot_be_read_and_the_points_still_show()
    {
        var rig = await HistoryRig.StartAsync();
        rig.Backend.FailNext<IGamesApi>(nameof(IGamesApi.GetStorageUsageAsync), ErrorInfo.From(new InvalidOperationException("No size.")));

        rig.Show();

        Assert.False(rig.Screen.HasFooter);
        Assert.Null(rig.Screen.FooterText);
        Assert.Equal(HistoryState.Loaded, rig.Screen.State);
    }

    [Fact]
    public async Task The_screen_is_the_history_destination()
    {
        var rig = await HistoryRig.StartAsync();

        Assert.Equal(NavDestination.History, rig.Screen.Destination);
    }
}
