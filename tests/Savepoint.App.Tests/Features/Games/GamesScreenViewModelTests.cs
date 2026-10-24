using Savepoint.App.Features.Games;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

public class GamesScreenViewModelTests
{
    private const string _dash = "-";

    private static FakeBackend WithProfiles(params (Profile Profile, ProfileRowStats Stats)[] profiles)
    {
        var scenario = ScenarioBuilder.Empty()
            .WithSettings(MockupScenario.Settings)
            .WithVolume(210 * MockupScenario.Gb, 131 * MockupScenario.Gb)
            .WithGame(MockupScenario.Caretaker, 9_400_000_000);
        foreach (var (profile, stats) in profiles)
        {
            scenario.WithProfile(profile, stats);
        }

        return new FakeBackend(scenario);
    }

    private static Profile Caretaker(string id, string name) => new(id, MockupScenario.CaretakerId, name, $"profile/{id}");

    // Assert.All passes on an empty list, so a test that checks every row first proves there are rows.
    private static IReadOnlyList<ProfileRowViewModel> Rows(GamesRig rig)
    {
        Assert.NotEmpty(rig.Screen.Profiles);
        return [.. rig.Screen.Profiles];
    }

    // ---- The profile table

    [Fact]
    public async Task The_table_lists_the_selected_games_profiles_in_catalog_order()
    {
        var rig = await GamesRig.StartAsync();

        Assert.Equal(["Modded playthrough - 7-2-26", "Vanilla - latest update", "Survival test - 5-14-26"], rig.Screen.Profiles.Select(row => row.Name));
        Assert.True(rig.Screen.ShowTable);
        Assert.False(rig.Screen.ShowEmptyState);
    }

    // Holds a read of the selected game's profiles until the test lets it go, as a status scan of a large install does.
    private static TaskCompletionSource Hold(Action<Func<CancellationToken, Task>> set)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        set(token => release.Task.WaitAsync(token));
        return release;
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "The screen never settled.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Switching_to_a_game_says_it_is_loading_until_the_first_read_returns()
    {
        var rig = await GamesRig.StartAsync();
        var release = Hold(gate => rig.Backend.Profiles.ListGate = gate);

        rig.Shell.SelectGame(MockupScenario.SatisfactoryId);

        Assert.True(rig.Screen.IsLoading);
        Assert.True(rig.Screen.ShowLoading);
        Assert.False(rig.Screen.ShowTable);
        Assert.False(rig.Screen.ShowEmptyState);
        Assert.Empty(rig.Screen.Profiles);

        release.SetResult();
        await UntilAsync(() => !rig.Screen.IsLoading);

        Assert.False(rig.Screen.ShowLoading);
        Assert.True(rig.Screen.ShowTable);
    }

    [Fact]
    public async Task The_rows_show_as_soon_as_the_profile_list_arrives_and_fill_in_when_the_stats_land()
    {
        var rig = await GamesRig.StartAsync();
        var release = Hold(gate => rig.Backend.Profiles.StatsGate = gate);

        rig.Shell.SelectGame(MockupScenario.SatisfactoryId);
        await UntilAsync(() => rig.Screen.Profiles.Count == 2);

        Assert.True(rig.Screen.IsLoading);
        Assert.False(rig.Screen.ShowLoading);
        Assert.True(rig.Screen.ShowTable);
        Assert.Equal(["Main factory - 6-1-26", "Update 1.1 test - 6-20-26"], rig.Screen.Profiles.Select(row => row.Name));
        Assert.Equal([_dash, _dash], rig.Screen.Profiles.Select(row => row.VersionText));

        release.SetResult();
        await UntilAsync(() => !rig.Screen.IsLoading);

        Assert.Equal(["1.1.0.4", "1.1.0.4"], rig.Screen.Profiles.Select(row => row.VersionText));
        Assert.Equal([true, false], rig.Screen.Profiles.Select(row => row.IsActive));
    }

    [Fact]
    public async Task Each_row_says_what_state_the_profile_is_in()
    {
        var rig = await GamesRig.StartAsync();

        Assert.Equal(["Loaded now \u00b7 4 snapshots", "1 snapshot", "New changes \u00b7 3 snapshots"], rig.Screen.Profiles.Select(row => row.SecondaryText));
        Assert.Equal([true, false, false], rig.Screen.Profiles.Select(row => row.IsActive));
        Assert.Equal([true, false, false], rig.Screen.Profiles.Select(row => row.IsActiveLine));
        Assert.Equal([false, false, true], rig.Screen.Profiles.Select(row => row.IsUnsavedLine));
        Assert.Equal([false, true, false], rig.Screen.Profiles.Select(row => row.IsPlainLine));
    }

    [Fact]
    public async Task Each_row_shows_version_mod_files_and_when_it_was_last_played()
    {
        var rig = await GamesRig.StartAsync();

        Assert.Equal(["1.6.640", "1.6.1170", "1.6.640"], rig.Screen.Profiles.Select(row => row.VersionText));
        Assert.Equal(["212", _dash, "189"], rig.Screen.Profiles.Select(row => row.ModFilesText));
        Assert.Equal(["3 days ago", "6 weeks ago", "12 weeks ago"], rig.Screen.Profiles.Select(row => row.LastPlayedText));
    }

    [Fact]
    public async Task A_version_the_user_typed_wins_over_the_detected_one()
    {
        var rig = await GamesRig.StartAsync();
        FakeAssert.Ok(await rig.Backend.Profiles.UpdateAsync(MockupScenario.SkyrimId, MockupScenario.ModdedId, new ProfileChanges("Modded playthrough - 7-2-26", null, "1.7.0-custom")));

        await rig.Screen.ReloadAsync();

        var row = rig.Screen.Profiles[0];
        Assert.Equal("1.7.0-custom", row.VersionText);
        Assert.Equal("1.7.0-custom", row.VersionOverride);
        Assert.Equal("1.6.640", row.DetectedVersion);
    }

    [Fact]
    public async Task A_profile_with_nothing_known_shows_dashes_and_no_second_line()
    {
        var backend = WithProfiles((Caretaker("p1", "No details"), new ProfileRowStats("p1", false, [], null, null, null, null, null, false)));
        var rig = await GamesRig.StartAsync(backend);

        var row = rig.Screen.Profiles.Single();

        Assert.Equal(string.Empty, row.SecondaryText);
        Assert.Equal(_dash, row.VersionText);
        Assert.Equal(_dash, row.ModFilesText);
        Assert.Equal(_dash, row.LastPlayedText);
    }

    [Fact]
    public async Task New_changes_come_before_loaded_in_the_second_line_and_the_count_is_left_out_when_unknown()
    {
        var backend = WithProfiles(
            (Caretaker("p1", "Both"), new ProfileRowStats("p1", true, [RepoRole.Install], 4, null, null, null, null, false)),
            (Caretaker("p2", "Unsaved without saves folder"), new ProfileRowStats("p2", false, [RepoRole.External], null, null, null, null, null, false)),
            (Caretaker("p3", "Active without saves folder"), new ProfileRowStats("p3", true, [], null, null, null, null, null, false)));
        var rig = await GamesRig.StartAsync(backend);

        Assert.Equal(["New changes \u00b7 4 snapshots", "New changes", "Loaded now"], rig.Screen.Profiles.Select(row => row.SecondaryText));
        Assert.True(rig.Screen.Profiles[0].IsActive);
        Assert.True(rig.Screen.Profiles[0].IsUnsavedLine);
        Assert.False(rig.Screen.Profiles[0].IsActiveLine);
    }

    [Fact]
    public async Task Rows_describe_themselves_to_assistive_technology()
    {
        var rig = await GamesRig.StartAsync();
        var row = rig.Screen.Profiles[0];

        Assert.Equal("Play Modded playthrough - 7-2-26", row.PlayAutomationName);
        Assert.Equal("More actions for Modded playthrough - 7-2-26", row.MoreActionsAutomationName);
        Assert.Contains("Modded playthrough - 7-2-26", row.AutomationName);
        Assert.Contains("Loaded now \u00b7 4 snapshots", row.AutomationName);
        Assert.Contains("1.6.640", row.AutomationName);
        Assert.Contains("212", row.AutomationName);
        Assert.Contains("3 days ago", row.AutomationName);
    }

    // ---- Which game

    [Fact]
    public async Task The_header_names_the_selected_game_and_its_folder()
    {
        var rig = await GamesRig.StartAsync();

        Assert.True(rig.Screen.HasGame);
        Assert.Equal("Skyrim Special Edition", rig.Screen.GameName);
        Assert.Equal(@"C:\Games\SkyrimSE", rig.Screen.InstallPath);
        Assert.Equal("Profiles for Skyrim Special Edition", rig.Screen.LibraryTitle);
    }

    [Fact]
    public async Task Choosing_another_game_shows_its_profiles()
    {
        var rig = await GamesRig.StartAsync();

        rig.Shell.SelectGame(MockupScenario.SatisfactoryId);

        Assert.Equal("Satisfactory", rig.Screen.GameName);
        Assert.Equal(["Main factory - 6-1-26", "Update 1.1 test - 6-20-26"], rig.Screen.Profiles.Select(row => row.Name));
    }

    [Fact]
    public async Task The_previous_games_rows_are_gone_while_the_new_game_loads()
    {
        var backend = FakeBackend.Mockup();
        var gated = new GatedProfilesApi(backend.Profiles);
        var rig = await GamesRig.StartAsync(backend, profiles: gated);
        var release = gated.Hold(MockupScenario.SatisfactoryId);

        rig.Shell.SelectGame(MockupScenario.SatisfactoryId);

        Assert.Empty(rig.Screen.Profiles);
        Assert.True(rig.Screen.IsLoading);
        Assert.False(rig.Screen.ShowEmptyState);
        Assert.Equal("Satisfactory", rig.Screen.GameName);
        release();
        await rig.Screen.ReloadAsync();
        Assert.False(rig.Screen.IsLoading);
        Assert.Equal(2, rig.Screen.Profiles.Count);
    }

    [Fact]
    public async Task A_slow_read_for_a_game_that_is_no_longer_selected_never_lands()
    {
        var backend = FakeBackend.Mockup();
        var gated = new GatedProfilesApi(backend.Profiles);
        var rig = await GamesRig.StartAsync(backend, profiles: gated);
        var releaseSkyrim = gated.Hold(MockupScenario.SkyrimId);
        var slowRead = rig.Screen.ReloadAsync();

        rig.Shell.SelectGame(MockupScenario.SatisfactoryId);
        releaseSkyrim();
        await slowRead;

        Assert.Equal(["Main factory - 6-1-26", "Update 1.1 test - 6-20-26"], rig.Screen.Profiles.Select(row => row.Name));
        Assert.Equal("Satisfactory", rig.Screen.GameName);
    }

    [Fact]
    public async Task An_empty_library_has_no_game_and_no_rows()
    {
        var rig = await GamesRig.StartAsync(FakeBackend.Empty());

        Assert.False(rig.Screen.HasGame);
        Assert.Empty(rig.Screen.Profiles);
        Assert.False(rig.Screen.ShowTable);
        Assert.False(rig.Screen.ShowEmptyState);
        Assert.False(rig.Screen.HasStorageFooter);
    }

    [Fact]
    public async Task A_game_without_profiles_shows_the_prompt_to_create_the_first()
    {
        var rig = await GamesRig.StartAsync(WithProfiles());

        Assert.True(rig.Screen.ShowEmptyState);
        Assert.False(rig.Screen.ShowTable);
        Assert.Empty(rig.Screen.Profiles);
    }

    // ---- Keeping up with the library

    [Fact]
    public async Task Showing_the_screen_reads_the_profiles_again()
    {
        var rig = await GamesRig.StartAsync();
        FakeAssert.Ok(await rig.Backend.Profiles.UpdateAsync(MockupScenario.SkyrimId, MockupScenario.VanillaId, new ProfileChanges("Vanilla, renamed", null, null)));

        rig.Screen.OnShown();

        Assert.Equal("Vanilla, renamed", rig.Screen.Profiles[1].Name);
    }

    [Fact]
    public async Task A_library_refresh_updates_the_rows_in_place()
    {
        var rig = await GamesRig.StartAsync();
        var before = rig.Screen.Profiles.ToList();
        FakeAssert.Ok(await rig.Backend.Profiles.UpdateAsync(MockupScenario.SkyrimId, MockupScenario.ModdedId, new ProfileChanges("Renamed", null, null)));

        await rig.Shell.RefreshLibraryAsync();

        Assert.Equal("Renamed", rig.Screen.Profiles[0].Name);
        Assert.Equal(before, rig.Screen.Profiles);
        Assert.All(before.Zip(rig.Screen.Profiles), pair => Assert.Same(pair.First, pair.Second));
    }

    [Fact]
    public async Task A_new_profile_appears_and_a_deleted_one_goes_after_a_refresh()
    {
        var rig = await GamesRig.StartAsync();
        FakeAssert.Ok(await rig.Backend.Profiles.DuplicateAsync(MockupScenario.SkyrimId, MockupScenario.VanillaId, "Vanilla copy"));
        FakeAssert.Ok(await rig.Backend.Profiles.DeleteAsync(MockupScenario.SkyrimId, MockupScenario.SurvivalId));

        await rig.Shell.RefreshLibraryAsync();

        Assert.Equal(["Modded playthrough - 7-2-26", "Vanilla - latest update", "Vanilla copy"], rig.Screen.Profiles.Select(row => row.Name));
    }

    // ---- When a read fails

    [Fact]
    public async Task A_failed_profile_list_shows_a_card_with_a_way_to_try_again()
    {
        var rig = await GamesRig.StartAsync();
        rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.ListAsync), MockupScenario.FolderGoneError);

        await rig.Screen.ReloadAsync();

        Assert.NotNull(rig.Screen.LoadError);
        Assert.Equal("Try again", rig.Screen.LoadError.Actions.Single().Label);
        Assert.False(rig.Screen.ShowTable);
        Assert.False(rig.Screen.ShowEmptyState);

        rig.Screen.LoadError.Actions.Single().Command.Execute(null);

        Assert.Null(rig.Screen.LoadError);
        Assert.Equal(3, rig.Screen.Profiles.Count);
    }

    [Fact]
    public async Task Failed_row_stats_still_list_the_profiles_and_say_what_went_wrong()
    {
        var rig = await GamesRig.StartAsync();
        rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.GetRowStatsAsync), MockupScenario.FileInUseError);

        await rig.Screen.ReloadAsync();

        Assert.Equal(3, rig.Screen.Profiles.Count);
        Assert.Equal(string.Empty, rig.Screen.Profiles[0].SecondaryText);
        Assert.Equal(_dash, rig.Screen.Profiles[0].VersionText);
        Assert.Single(rig.ToastTexts);
        Assert.StartsWith("Something else on your computer", rig.ToastTexts[0]);
    }

    // ---- The storage footer

    [Fact]
    public async Task The_footer_says_how_much_of_the_storage_folder_the_game_uses()
    {
        var rig = await GamesRig.StartAsync();

        Assert.True(rig.Screen.HasStorageFooter);
        Assert.Equal("This game's snapshots use 41.2 GB on a 210 GB drive", rig.Screen.StorageFooterText);
    }

    [Fact]
    public async Task The_footer_follows_the_selected_game()
    {
        var rig = await GamesRig.StartAsync();

        rig.Shell.SelectGame(MockupScenario.SatisfactoryId);

        Assert.Equal("This game's snapshots use 28.7 GB on a 210 GB drive", rig.Screen.StorageFooterText);
    }

    [Fact]
    public async Task The_footer_offers_free_up_space_for_the_selected_game_and_follows_the_selection()
    {
        var rig = await GamesRig.StartAsync();
        Assert.Equal("Free up space for Skyrim Special Edition", rig.Screen.FreeUp.Name);

        await rig.Screen.FreeUp.FreeUpCommand.ExecuteAsync(null);

        Assert.Equal([MockupScenario.SkyrimId], rig.Backend.Jobs.List().Where(job => job.Kind == JobKind.Compact).Select(job => job.GameId));
        Assert.True(rig.Screen.FreeUp.IsRunning);

        rig.Shell.SelectGame(MockupScenario.SatisfactoryId);

        Assert.Equal("Free up space for Satisfactory", rig.Screen.FreeUp.Name);
        Assert.False(rig.Screen.FreeUp.IsRunning);
    }

    [Fact]
    public async Task When_a_free_up_run_ends_the_footer_reads_the_games_storage_use_again()
    {
        var rig = await GamesRig.StartAsync();
        await rig.Screen.FreeUp.FreeUpCommand.ExecuteAsync(null);
        var job = rig.Backend.Jobs.LastStarted(JobKind.Compact).JobId;
        rig.Backend.State.GameBytes[MockupScenario.SkyrimId] = 10_000_000_000;

        rig.Backend.Jobs.Complete(job);
        await Task.Yield();

        Assert.Equal("This game's snapshots use 10 GB on a 210 GB drive", rig.Screen.StorageFooterText);
    }

    [Fact]
    public async Task A_free_up_run_that_cannot_start_is_a_toast_like_the_screens_other_failures()
    {
        var rig = await GamesRig.StartAsync();
        rig.Backend.FailNext<IGamesApi>(nameof(IGamesApi.OptimizeStorageAsync), MockupScenario.FileInUseError);

        await rig.Screen.FreeUp.FreeUpCommand.ExecuteAsync(null);

        Assert.Single(rig.ToastTexts);
        Assert.StartsWith("Something else on your computer", rig.ToastTexts[0]);
    }

    [Fact]
    public async Task Showing_the_screen_again_forgets_how_the_last_free_up_run_ended()
    {
        var rig = await GamesRig.StartAsync();
        await rig.Screen.FreeUp.FreeUpCommand.ExecuteAsync(null);
        rig.Backend.Jobs.Complete(rig.Backend.Jobs.LastStarted(JobKind.Compact).JobId);
        Assert.True(rig.Screen.FreeUp.HasStatus);

        rig.Screen.OnShown();

        Assert.False(rig.Screen.FreeUp.HasStatus);
    }

    [Fact]
    public async Task A_disposed_screen_stops_following_free_up_runs()
    {
        var rig = await GamesRig.StartAsync();
        await rig.Screen.FreeUp.FreeUpCommand.ExecuteAsync(null);

        rig.Screen.Dispose();
        rig.Backend.Jobs.Complete(rig.Backend.Jobs.LastStarted(JobKind.Compact).JobId);

        Assert.True(rig.Screen.FreeUp.IsRunning);
    }

    [Fact]
    public async Task A_footer_that_cannot_be_read_is_left_out()
    {
        var rig = await GamesRig.StartAsync();
        rig.Backend.FailNext<IGamesApi>(nameof(IGamesApi.GetStorageUsageAsync), MockupScenario.FileInUseError);

        await rig.Screen.ReloadAsync();

        Assert.False(rig.Screen.HasStorageFooter);
        Assert.Equal(3, rig.Screen.Profiles.Count);
        Assert.Empty(rig.ToastTexts);
    }

    // ---- Play

    [Fact]
    public async Task Play_is_available_once_the_launch_flow_is_plugged_in()
    {
        var rig = await GamesRig.StartAsync();

        Assert.All(Rows(rig), row => Assert.True(row.CanPlay));
        Assert.All(Rows(rig), row => Assert.Equal("Play", row.PlayLabel));
    }

    [Fact]
    public async Task Play_is_unavailable_while_the_flow_has_a_session_for_the_game()
    {
        var rig = await GamesRig.StartAsync();

        rig.LaunchFlow!.SetActive(MockupScenario.SkyrimId, true);

        Assert.All(Rows(rig), row => Assert.False(row.CanPlay));
        Assert.All(Rows(rig), row => Assert.False(row.PlayCommand.CanExecute(null)));

        rig.LaunchFlow.SetActive(MockupScenario.SkyrimId, false);

        Assert.All(Rows(rig), row => Assert.True(row.CanPlay));
    }

    [Fact]
    public async Task A_session_in_another_game_does_not_block_play_here()
    {
        var rig = await GamesRig.StartAsync();

        rig.LaunchFlow!.SetActive(MockupScenario.SatisfactoryId, true);

        Assert.All(Rows(rig), row => Assert.True(row.CanPlay));
    }

    [Fact]
    public async Task The_running_profile_says_so_and_nothing_can_be_played_meanwhile()
    {
        var rig = await GamesRig.StartAsync();
        FakeAssert.Ok(await rig.Backend.Launch.StartAsync(MockupScenario.SkyrimId, new LaunchTarget.ProfileTip(MockupScenario.ModdedId)));

        await rig.Screen.ReloadAsync();

        Assert.Equal(["Running", "Play", "Play"], rig.Screen.Profiles.Select(row => row.PlayLabel));
        Assert.Equal([true, false, false], rig.Screen.Profiles.Select(row => row.IsRunning));
        Assert.All(Rows(rig), row => Assert.False(row.CanPlay));
    }

    [Fact]
    public async Task Play_comes_back_when_the_session_ends_without_anything_else_reloading_the_table()
    {
        var rig = await GamesRig.StartAsync();
        var game = MockupScenario.SkyrimId;
        FakeAssert.Ok(await rig.Backend.Launch.StartAsync(game, new LaunchTarget.ProfileTip(MockupScenario.ModdedId)));
        rig.LaunchFlow!.SetActive(game, true);
        await rig.Screen.ReloadAsync();
        Assert.All(Rows(rig), row => Assert.False(row.CanPlay));

        // "Not now": the session is finished and nobody saves anything, so no job runs and no library refresh follows.
        rig.Backend.Launch.ReportRunning(game, MockupScenario.RunningSince);
        rig.Backend.Launch.ReportExited(game, MockupScenario.PlayDuration, 0);
        FakeAssert.Ok(await rig.Backend.Launch.CompleteSessionAsync(game, new SessionCompletion.Skip()));
        rig.LaunchFlow.SetActive(game, false);

        Assert.All(Rows(rig), row => Assert.False(row.IsRunning));
        Assert.All(Rows(rig), row => Assert.True(row.CanPlay));
    }

    // ---- What the launch flow shows below the table

    [Fact]
    public async Task A_launch_card_scrolls_with_the_profiles_in_a_pane_too_short_to_pin_it_and_is_pinned_again_when_it_grows()
    {
        var rig = await GamesRig.StartAsync();
        var banner = new object();
        rig.LaunchFlow!.SetInline(MockupScenario.SkyrimId, banner);
        rig.Screen.SetPaneHeight(GamesScreenViewModel.PinnedCardMinHeight);
        Assert.Same(banner, rig.Screen.PinnedContent);
        Assert.Null(rig.Screen.ScrollingContent);

        rig.Screen.SetPaneHeight(GamesScreenViewModel.PinnedCardMinHeight - 1);

        Assert.Null(rig.Screen.PinnedContent);
        Assert.False(rig.Screen.HasPinnedContent);
        Assert.Same(banner, rig.Screen.ScrollingContent);
        Assert.True(rig.Screen.HasScrollingContent);

        rig.Screen.SetPaneHeight(GamesScreenViewModel.PinnedCardMinHeight);

        Assert.Same(banner, rig.Screen.PinnedContent);
        Assert.Null(rig.Screen.ScrollingContent);
    }

    [Fact]
    public async Task The_launch_flows_inline_content_for_the_selected_game_shows_above_the_table()
    {
        var rig = await GamesRig.StartAsync();
        Assert.False(rig.Screen.HasInlineContent);
        var banner = new object();

        rig.LaunchFlow!.SetInline(MockupScenario.SkyrimId, banner);

        Assert.Same(banner, rig.Screen.InlineContent);
        Assert.True(rig.Screen.HasInlineContent);

        rig.LaunchFlow.SetInline(MockupScenario.SkyrimId, null);

        Assert.Null(rig.Screen.InlineContent);
        Assert.False(rig.Screen.HasInlineContent);
    }

    [Fact]
    public async Task Inline_content_for_another_game_is_not_shown_until_that_game_is_selected()
    {
        var rig = await GamesRig.StartAsync();
        var card = new object();

        rig.LaunchFlow!.SetInline(MockupScenario.SatisfactoryId, card);

        Assert.Null(rig.Screen.InlineContent);

        rig.Shell.SelectGame(MockupScenario.SatisfactoryId);

        Assert.Same(card, rig.Screen.InlineContent);
    }

    [Fact]
    public async Task Content_arriving_on_a_background_thread_is_handed_to_the_dispatcher()
    {
        var dispatcher = new QueueingDispatcher();
        var rig = await GamesRig.StartAsync(dispatcher: dispatcher);
        dispatcher.RunAll();
        var banner = new object();

        rig.LaunchFlow!.SetInline(MockupScenario.SkyrimId, banner);

        Assert.Null(rig.Screen.InlineContent);
        dispatcher.RunAll();
        Assert.Same(banner, rig.Screen.InlineContent);
    }

    // ---- Narrow windows

    [Theory]
    [InlineData(760, true, true)]
    [InlineData(ProfileTableColumns.ModFilesMinWidth, true, true)]
    [InlineData(620, true, false)]
    [InlineData(ProfileTableColumns.VersionMinWidth, true, false)]
    [InlineData(400, false, false)]
    public async Task A_narrow_table_drops_the_mod_files_column_before_the_version_column(double width, bool showVersion, bool showModFiles)
    {
        var rig = await GamesRig.StartAsync();

        rig.Screen.SetTableWidth(width);

        Assert.Equal(showVersion, rig.Screen.Columns.ShowVersion);
        Assert.Equal(showModFiles, rig.Screen.Columns.ShowModFiles);
        Assert.All(Rows(rig), row => Assert.Same(rig.Screen.Columns, row.Columns));
    }

    [Fact]
    public async Task A_table_that_grows_again_gets_its_columns_back()
    {
        var rig = await GamesRig.StartAsync();
        rig.Screen.SetTableWidth(400);

        rig.Screen.SetTableWidth(800);

        Assert.True(rig.Screen.Columns.ShowVersion);
        Assert.True(rig.Screen.Columns.ShowModFiles);
    }

    [Theory]
    [InlineData(GameHeaderLayout.SidebarWordsMinWidth, false)]
    [InlineData(GameHeaderLayout.SidebarWordsMinWidth - 1, true)]
    [InlineData(1400, false)]
    [InlineData(532, true)]
    public async Task A_narrow_sidebar_header_shows_icons_so_the_name_keeps_its_room(double width, bool compact)
    {
        var rig = await GamesRig.StartAsync();

        rig.Screen.SetHeaderWidth(width);

        Assert.Equal(compact, rig.Screen.Header.IsCompact);
    }

    [Fact]
    public async Task The_library_header_needs_less_room_than_the_sidebar_header()
    {
        var rig = await GamesRig.StartAsync();
        await rig.Shell.SetLayoutAsync(ShellLayout.Library);

        rig.Screen.SetHeaderWidth(GameHeaderLayout.LibraryWordsMinWidth);
        Assert.False(rig.Screen.Header.IsCompact);

        rig.Screen.SetHeaderWidth(GameHeaderLayout.LibraryWordsMinWidth - 1);
        Assert.True(rig.Screen.Header.IsCompact);
    }

    [Fact]
    public async Task Switching_layout_judges_the_same_width_again()
    {
        var rig = await GamesRig.StartAsync();
        rig.Screen.SetHeaderWidth(700);
        Assert.True(rig.Screen.Header.IsCompact);

        await rig.Shell.SetLayoutAsync(ShellLayout.Library);

        Assert.False(rig.Screen.Header.IsCompact);
    }

    // ---- Header

    [Fact]
    public async Task The_header_follows_the_layout()
    {
        var rig = await GamesRig.StartAsync();
        Assert.True(rig.Screen.IsSidebarLayout);
        Assert.False(rig.Screen.IsLibraryLayout);
        var changed = new List<string?>();
        rig.Screen.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await rig.Shell.SetLayoutAsync(ShellLayout.Library);

        Assert.True(rig.Screen.IsLibraryLayout);
        Assert.False(rig.Screen.IsSidebarLayout);
        Assert.Contains(nameof(GamesScreenViewModel.IsLibraryLayout), changed);
        Assert.Contains(nameof(GamesScreenViewModel.IsSidebarLayout), changed);
    }

    [Fact]
    public async Task The_footer_is_drawn_in_the_sidebar_layout_only()
    {
        var rig = await GamesRig.StartAsync();
        Assert.True(rig.Screen.ShowStorageFooter);
        var changed = new List<string?>();
        rig.Screen.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await rig.Shell.SetLayoutAsync(ShellLayout.Library);

        Assert.False(rig.Screen.ShowStorageFooter);
        Assert.True(rig.Screen.HasStorageFooter);
        Assert.Contains(nameof(GamesScreenViewModel.ShowStorageFooter), changed);
    }

    [Fact]
    public async Task The_headers_overflow_button_is_named_after_the_game()
    {
        var rig = await GamesRig.StartAsync();
        Assert.Equal("More actions for Skyrim Special Edition", rig.Screen.GameMoreActionsName);

        rig.Shell.SelectGame(MockupScenario.SatisfactoryId);

        Assert.Equal("More actions for Satisfactory", rig.Screen.GameMoreActionsName);
    }

    [Fact]
    public async Task The_history_chip_goes_to_the_history_destination()
    {
        var rig = await GamesRig.StartAsync();

        rig.Screen.ShowHistoryCommand.Execute(null);

        Assert.Equal(NavDestination.History, rig.Shell.Destination);
        Assert.Equal(MockupScenario.SkyrimId, rig.Shell.SelectedGame?.Id);
    }

    [Fact]
    public async Task The_activity_chip_opens_activity_on_the_selected_game()
    {
        var rig = await GamesRig.StartAsync();
        var requests = new List<ActivityRequest>();
        rig.Shell.ActivityRequested += (_, request) => requests.Add(request);
        rig.Shell.SelectGame(MockupScenario.SatisfactoryId);

        rig.Screen.ShowActivityCommand.Execute(null);

        Assert.Equal(NavDestination.Activity, rig.Shell.Destination);
        Assert.Equal([new ActivityRequest(MockupScenario.SatisfactoryId)], requests);
    }

    [Fact]
    public async Task Edit_game_opens_the_editor_pane_for_the_selected_game()
    {
        var rig = await GamesRig.StartAsync();

        rig.Screen.EditGameCommand.Execute(null);

        var pane = Assert.Single(rig.Editor!.Created);
        Assert.Equal(new EditorPane("existing", MockupScenario.SkyrimId, null), pane);
        Assert.Same(pane, rig.Shell.Content);
    }

    [Fact]
    public async Task A_request_to_add_a_game_opens_the_editor_for_a_new_one()
    {
        var rig = await GamesRig.StartAsync();
        var prefill = new GameDefinition(Id: "template-game", Name: "From a template");

        rig.Shell.RequestAddGame(prefill);

        var pane = Assert.Single(rig.Editor!.Created);
        Assert.Equal("new", pane.Kind);
        Assert.Same(prefill, pane.Prefill);
        Assert.Same(pane, rig.Shell.Content);
    }

    [Fact]
    public async Task A_failure_the_flow_says_the_editor_fixes_opens_it_for_that_game()
    {
        var rig = await GamesRig.StartAsync();

        rig.LaunchFlow!.RaiseEditGameRequested(MockupScenario.SatisfactoryId);

        Assert.Equal(MockupScenario.SatisfactoryId, rig.Shell.SelectedGame?.Id);
        var pane = Assert.Single(rig.Editor!.Created);
        Assert.Equal(new EditorPane("existing", MockupScenario.SatisfactoryId, null), pane);
        Assert.Same(pane, rig.Shell.Content);
    }

    [Fact]
    public async Task The_editor_the_flow_asks_for_opens_on_Games_whichever_screen_is_showing()
    {
        var rig = await GamesRig.StartAsync();
        rig.Shell.Navigate(NavDestination.History);

        rig.LaunchFlow!.RaiseEditGameRequested(MockupScenario.SkyrimId);

        Assert.Equal(NavDestination.Games, rig.Shell.Destination);
        Assert.Same(Assert.Single(rig.Editor!.Created), rig.Shell.Content);
    }

    [Fact]
    public async Task A_request_to_edit_a_game_that_is_not_in_the_library_is_ignored()
    {
        var rig = await GamesRig.StartAsync();

        rig.LaunchFlow!.RaiseEditGameRequested("no-such-game");

        Assert.Empty(rig.Editor!.Created);
        Assert.Same(rig.Screen, rig.Shell.Content);
    }

    [Fact]
    public async Task A_disposed_screen_ignores_requests_to_edit()
    {
        var rig = await GamesRig.StartAsync();
        rig.Screen.Dispose();

        rig.LaunchFlow!.RaiseEditGameRequested(MockupScenario.SkyrimId);

        Assert.Empty(rig.Editor!.Created);
    }

    [Fact]
    public async Task A_disposed_screen_stops_following_the_shell()
    {
        var rig = await GamesRig.StartAsync();
        rig.Screen.Dispose();

        rig.Shell.SelectGame(MockupScenario.SatisfactoryId);
        rig.LaunchFlow!.SetInline(MockupScenario.SkyrimId, new object());

        Assert.Equal("Skyrim Special Edition", rig.Screen.GameName);
        Assert.Null(rig.Screen.InlineContent);
    }
}
