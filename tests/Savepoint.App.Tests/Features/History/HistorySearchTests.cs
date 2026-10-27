using Savepoint.App.Features.Common.Search;
using Savepoint.App.Features.History;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.History;

public class HistorySearchTests
{
    private const string _blackreach = "Cleared Blackreach";
    private const string _finalBoss = "Before the final boss";
    private const string _modsUpdated = "14 mod files updated";
    private const string _nolvus = "Fresh start with Nolvus";
    private const string _vanilla = "Vanilla 1.6.1170";
    private const string _deepCave = "Deep cave run";
    private const string _firstNight = "First night";
    private const string _survival = "Survival test - 5-14-26";

    private readonly ManualTimeProvider _clock = new(ScenarioBuilder.DefaultNow);

    private async Task<HistoryRig> ShownAsync(FakeBackend? backend = null) => await HistoryRig.ShownAsync(backend, _clock);

    private static FakeBackend WithRemoved() => new(RemovedSnapshotSamples.Scenario());

    private static List<string> Cards(HistoryRig rig) => [.. rig.Screen.Groups.SelectMany(group => group.Cards).Select(card => card.Name)];

    private static List<string> SaveNames(HistoryRig rig) => [.. rig.Screen.Saves.Select(save => save.Name)];

    private static void Choose(HistoryRig rig, HistoryKindFilter kind) =>
        rig.Screen.SelectedKindOption = rig.Screen.KindOptions.Single(option => option.Filter == kind);

    [Fact]
    public async Task With_nothing_typed_every_snapshot_is_listed_newest_first()
    {
        var rig = await ShownAsync();

        Assert.Equal([_finalBoss, _blackreach, _modsUpdated, _nolvus, _vanilla, _deepCave, _firstNight, _survival], Cards(rig));
        Assert.Equal(4, rig.Screen.Timeline.Count);
        Assert.Equal(5, rig.Screen.Saves.Count);
        Assert.True(rig.Screen.ShowSearch);
        Assert.False(rig.Screen.IsSearchEmpty);
        Assert.Equal(HistoryKindFilter.All, rig.Screen.SelectedKindOption?.Filter);
    }

    [Fact]
    public async Task Typing_keeps_the_snapshots_with_every_typed_word_in_their_name_in_the_cards_and_both_lists()
    {
        var rig = await ShownAsync();

        rig.Screen.Search.Text = "BLACKREACH";

        Assert.Equal([_blackreach], Cards(rig));
        Assert.Equal([_blackreach], SaveNames(rig));
        Assert.Empty(rig.Screen.Timeline);
    }

    [Fact]
    public async Task Typing_also_finds_a_snapshot_by_the_profile_it_belongs_to()
    {
        var rig = await ShownAsync();

        rig.Screen.Search.Text = "survival";

        Assert.Equal([_deepCave, _firstNight, _survival], Cards(rig));
    }

    // Gives a sample profile notes, which History reads with the profile list.
    private static ScenarioBuilder WithNotes(ScenarioBuilder scenario, string profileId, string notes)
    {
        var index = scenario.Profiles.FindIndex(entry => entry.Profile.Id == profileId);
        scenario.Profiles[index] = (scenario.Profiles[index].Profile with { Notes = notes }, scenario.Profiles[index].Stats);
        return scenario;
    }

    [Fact]
    public async Task Typing_words_from_a_profiles_notes_finds_the_snapshots_that_belong_to_it()
    {
        var rig = await ShownAsync(new FakeBackend(WithNotes(ScenarioBuilder.Sample(), SampleScenario.SurvivalId, "Hunger and cold are on")));

        rig.Screen.Search.Text = "HUNGER cold";

        Assert.Equal([_deepCave, _firstNight, _survival], Cards(rig));
    }

    [Fact]
    public async Task Words_may_come_from_a_snapshots_own_text_and_from_its_profiles_notes_together()
    {
        var rig = await ShownAsync(new FakeBackend(WithNotes(ScenarioBuilder.Sample(), SampleScenario.SurvivalId, "Hunger and cold are on")));

        rig.Screen.Search.Text = "hunger cave";

        Assert.Equal([_deepCave], Cards(rig));
    }

    [Fact]
    public async Task A_recently_removed_snapshot_is_found_by_the_notes_of_the_profile_it_left()
    {
        var rig = await ShownAsync(new FakeBackend(WithNotes(RemovedSnapshotSamples.Scenario(), SampleScenario.VanillaId, "Unmodded baseline")));

        rig.Screen.Search.Text = "unmodded";

        Assert.Equal([_blackreach], rig.Screen.Removed.Select(removed => removed.Name));
    }

    [Fact]
    public async Task Typing_a_year_and_month_finds_the_snapshots_taken_in_that_month()
    {
        var rig = await ShownAsync();

        rig.Screen.Search.Text = "2026-06";
        Assert.Equal([_vanilla], Cards(rig));

        rig.Screen.Search.Text = "2026-07-04";
        Assert.Equal([_blackreach], Cards(rig));
    }

    [Fact]
    public async Task Typing_the_name_of_a_month_finds_the_snapshots_taken_in_that_month()
    {
        var rig = await ShownAsync();

        rig.Screen.Search.Text = "may";
        Assert.Equal([_deepCave, _firstNight, _survival], Cards(rig));

        rig.Screen.Search.Text = "June";
        Assert.Equal([_vanilla], Cards(rig));
    }

    [Fact]
    public async Task A_date_is_read_in_the_players_time_zone_not_in_utc()
    {
        var rig = await ShownAsync();

        // 11:42 PM on Jul 5 in the sample zone is already Jul 6 in UTC.
        rig.Screen.Search.Text = "2026-07-05";

        Assert.Equal([_finalBoss], Cards(rig));
    }

    [Fact]
    public async Task Typing_also_finds_a_snapshot_by_what_it_holds()
    {
        var rig = await ShownAsync();

        rig.Screen.Search.Text = "1.6.1170";

        Assert.Equal([_vanilla], Cards(rig));
    }

    [Fact]
    public async Task Accents_in_a_name_or_in_the_search_make_no_difference()
    {
        var scenario = ScenarioBuilder.Sample().WithHistoryPoint(
            SampleScenario.SkyrimId,
            new HistoryPoint(new HistoryPointId("pt-cafe"), HistoryPointKind.SaveOnly, "Caf\u00e9 stop", ScenarioBuilder.DefaultNow, SampleScenario.ModdedId, "Modded playthrough - 7-2-26", new InstallState("1.6.640", 212), null, null, false, false));
        var rig = await ShownAsync(new FakeBackend(scenario));

        rig.Screen.Search.Text = "cafe";
        Assert.Equal(["Caf\u00e9 stop"], Cards(rig));

        rig.Screen.Search.Text = "CAF\u00c9";
        Assert.Equal(["Caf\u00e9 stop"], Cards(rig));
    }

    [Fact]
    public async Task The_Saves_choice_keeps_the_snapshots_that_hold_saves_and_Game_and_mods_those_that_hold_the_game()
    {
        var rig = await ShownAsync();

        Choose(rig, HistoryKindFilter.Saves);
        Assert.Equal([_finalBoss, _blackreach, _nolvus, _deepCave, _firstNight], Cards(rig));
        Assert.Equal(Cards(rig), SaveNames(rig));
        Assert.Single(rig.Screen.Timeline);

        Choose(rig, HistoryKindFilter.GameAndMods);
        Assert.Equal([_modsUpdated, _nolvus, _vanilla, _survival], Cards(rig));
        Assert.Equal(4, rig.Screen.Timeline.Count);
        Assert.Equal([_nolvus], SaveNames(rig));

        Choose(rig, HistoryKindFilter.All);
        Assert.Equal(8, Cards(rig).Count);
    }

    [Fact]
    public async Task The_kind_choice_and_the_typed_words_narrow_together()
    {
        var rig = await ShownAsync();
        Choose(rig, HistoryKindFilter.Saves);

        rig.Screen.Search.Text = "survival";

        Assert.Equal([_deepCave, _firstNight], Cards(rig));
    }

    [Fact]
    public async Task The_kind_group_offers_All_Saves_and_Game_and_mods_with_a_name()
    {
        var rig = await ShownAsync();

        Assert.Equal(["All", "Saves", "Game and mods"], rig.Screen.KindOptions.Select(option => option.Label));
        Assert.Equal("Kind of snapshot", rig.Screen.KindLabel);
        Assert.Equal("Search snapshots", rig.Screen.Search.Label);
    }

    [Fact]
    public async Task A_list_box_clearing_the_kind_choice_while_it_rebuilds_leaves_the_choice_as_it_was()
    {
        var rig = await ShownAsync();
        Choose(rig, HistoryKindFilter.Saves);

        rig.Screen.SelectedKindOption = null;

        Assert.Equal(HistoryKindFilter.Saves, rig.Screen.SelectedKindOption?.Filter);
        Assert.Equal(5, Cards(rig).Count);
    }

    [Fact]
    public async Task The_timeline_rail_ends_at_the_last_snapshot_the_search_leaves()
    {
        var rig = await ShownAsync();

        rig.Screen.Search.Text = "2026-07-02";

        Assert.Equal([false, true], rig.Screen.Timeline.Select(entry => entry.IsLast));
        Assert.Equal([true, false], rig.Screen.Timeline.Select(entry => entry.HasRail));
    }

    [Fact]
    public async Task Typing_keeps_the_groups_cards_and_saves_that_still_match_as_the_same_objects()
    {
        var rig = await ShownAsync();
        var groupsBefore = rig.Screen.Groups.ToList();
        var cardsBefore = rig.Screen.Groups.SelectMany(group => group.Cards).ToList();
        var savesBefore = rig.Screen.Saves.ToList();

        rig.Screen.Search.Text = "survival";

        var cards = rig.Screen.Groups.SelectMany(group => group.Cards).ToList();
        Assert.Equal([_deepCave, _firstNight, _survival], cards.Select(card => card.Name));
        AssertSameObjects(cards, cardsBefore);
        AssertSameObjects(rig.Screen.Groups, groupsBefore);
        AssertSameObjects(rig.Screen.Saves, savesBefore);
        Assert.NotEmpty(rig.Screen.Saves);
    }

    // The records here compare by value, so a rebuilt entry would pass a plain Contains.
    private static void AssertSameObjects<T>(IEnumerable<T> after, IReadOnlyList<T> before)
        where T : class =>
        Assert.All(after, item => Assert.Contains(before, other => ReferenceEquals(other, item)));

    [Fact]
    public async Task A_group_that_empties_and_fills_again_comes_back_as_the_same_group()
    {
        var rig = await ShownAsync();
        var earlier = rig.Screen.Groups.Single(group => group.Heading == "Earlier");

        rig.Screen.Search.Text = "2026-07-05";
        Assert.DoesNotContain(rig.Screen.Groups, group => group.Heading == "Earlier");
        rig.Screen.Search.ClearCommand.Execute(null);

        Assert.Same(earlier, rig.Screen.Groups.Single(group => group.Heading == "Earlier"));
    }

    [Fact]
    public async Task Typing_keeps_the_timeline_entries_that_still_match_and_moves_the_rails_end_without_replacing_them()
    {
        var rig = await ShownAsync();
        var before = rig.Screen.Timeline.ToList();

        rig.Screen.Search.Text = "2026-07-02";

        Assert.Equal(2, rig.Screen.Timeline.Count);
        AssertSameObjects(rig.Screen.Timeline, before);
        Assert.True(rig.Screen.Timeline[^1].IsLast);

        rig.Screen.Search.ClearCommand.Execute(null);

        Assert.Equal(before.Count, rig.Screen.Timeline.Count);
        AssertSameObjects(rig.Screen.Timeline, before);
        Assert.Equal([false, false, false, true], rig.Screen.Timeline.Select(entry => entry.IsLast));
    }

    [Fact]
    public async Task A_group_the_search_leaves_empty_is_left_out()
    {
        var rig = await ShownAsync();

        rig.Screen.Search.Text = "2026-05";
        Assert.Equal(["Earlier"], rig.Screen.Groups.Select(group => group.Heading));

        rig.Screen.Search.Text = "2026-07-05";
        Assert.Equal(["Last 7 days"], rig.Screen.Groups.Select(group => group.Heading));
    }

    [Fact]
    public async Task Recently_removed_snapshots_are_searched_by_name_and_by_the_profile_they_left()
    {
        var rig = await ShownAsync(WithRemoved());
        Assert.Equal(2, rig.Screen.RemovedTotal);

        rig.Screen.Search.Text = "blackreach";
        Assert.Equal([_blackreach], rig.Screen.Removed.Select(removed => removed.Name));

        rig.Screen.Search.Text = "vanilla";
        Assert.Equal([_blackreach], rig.Screen.Removed.Select(removed => removed.Name));

        rig.Screen.Search.Text = "final";
        Assert.Equal([_finalBoss], rig.Screen.Removed.Select(removed => removed.Name));
    }

    [Fact]
    public async Task Recently_removed_snapshots_are_found_by_the_date_they_were_taken()
    {
        var rig = await ShownAsync(WithRemoved());

        rig.Screen.Search.Text = "2026-07-06";

        Assert.Equal([_finalBoss], rig.Screen.Removed.Select(removed => removed.Name));
    }

    [Fact]
    public async Task The_kind_choice_narrows_the_recently_removed_too()
    {
        var rig = await ShownAsync(WithRemoved());

        Choose(rig, HistoryKindFilter.GameAndMods);

        Assert.Equal([_finalBoss], rig.Screen.Removed.Select(removed => removed.Name));
    }

    [Fact]
    public async Task The_removed_heading_says_how_many_of_them_the_search_leaves()
    {
        var rig = await ShownAsync(WithRemoved());
        Assert.Equal("Recently removed (2)", rig.Screen.RemovedHeading);

        rig.Screen.Search.Text = "blackreach";

        Assert.Equal("Recently removed (1 of 2)", rig.Screen.RemovedHeading);
    }

    [Fact]
    public async Task The_removed_section_stays_and_says_so_when_the_search_matches_none_of_them()
    {
        var rig = await ShownAsync(WithRemoved());

        rig.Screen.Search.Text = "deep cave";

        Assert.Empty(rig.Screen.Removed);
        Assert.True(rig.Screen.ShowRemoved);
        Assert.True(rig.Screen.ShowRemovedNoMatches);
        Assert.Equal("No removed snapshots match.", rig.Screen.RemovedNoMatchesText);
        Assert.Equal("Recently removed (0 of 2)", rig.Screen.RemovedHeading);
    }

    [Fact]
    public async Task A_search_nothing_matches_says_so_and_clearing_it_brings_everything_back()
    {
        var rig = await ShownAsync();

        rig.Screen.Search.Text = "zzz";

        Assert.True(rig.Screen.IsSearchEmpty);
        Assert.True(rig.Screen.HasPoints);
        Assert.True(rig.Screen.ShowSearch);
        Assert.Equal("No snapshots match.", rig.Screen.SearchNoMatchesText);
        Assert.Empty(Cards(rig));

        rig.Screen.Search.ClearCommand.Execute(null);

        Assert.False(rig.Screen.IsSearchEmpty);
        Assert.Equal(8, Cards(rig).Count);
    }

    [Fact]
    public async Task The_search_row_is_not_there_while_loading_or_without_snapshots()
    {
        var empty = await ShownAsync(new FakeBackend(ScenarioBuilder.Sample().WithGame(new Savepoint.Core.Catalog.GameDefinition(Id: "bare", Name: "Bare", Install: new Savepoint.Core.Catalog.InstallLocation(@"D:\Bare")))));
        empty.Shell.SelectGame("bare");
        await empty.Screen.ReloadAsync();

        Assert.True(empty.Screen.IsEmpty);
        Assert.False(empty.Screen.ShowSearch);
    }

    [Fact]
    public async Task The_search_row_shows_for_a_game_whose_only_snapshots_are_recently_removed()
    {
        var scenario = ScenarioBuilder.Sample().WithGame(new Savepoint.Core.Catalog.GameDefinition(Id: "bare", Name: "Bare", Install: new Savepoint.Core.Catalog.InstallLocation(@"D:\Bare")))
            .WithRemovedSnapshot("bare", RemovedSnapshotSamples.Replaced);
        var rig = await ShownAsync(new FakeBackend(scenario));
        rig.Shell.SelectGame("bare");
        await rig.Screen.ReloadAsync();

        Assert.True(rig.Screen.IsEmpty);
        Assert.True(rig.Screen.ShowSearch);
    }

    [Fact]
    public async Task The_search_box_names_Ctrl_F_in_its_tooltip_and_as_its_accelerator_key()
    {
        var rig = await ShownAsync();

        Assert.Equal("Search snapshots (Ctrl+F)", rig.Screen.Search.Tip);
        Assert.Equal("Ctrl+F", rig.Screen.Search.Shortcut);
    }

    [Fact]
    public async Task The_search_survives_a_profile_choice_and_a_new_read()
    {
        var rig = await ShownAsync();
        rig.Screen.Search.Text = "survival";

        await rig.Screen.ReloadAsync();
        rig.Screen.SelectedFilter = rig.Screen.Filters.Single(filter => filter.ProfileId == SampleScenario.VanillaId);
        await rig.Screen.ReloadAsync();

        Assert.Equal("survival", rig.Screen.Search.Text);
        Assert.Empty(Cards(rig));
        Assert.True(rig.Screen.IsSearchEmpty);

        rig.Screen.SelectedFilter = rig.Screen.Filters.Single(filter => filter.ProfileId == SampleScenario.SurvivalId);
        await rig.Screen.ReloadAsync();

        Assert.Equal([_deepCave, _firstNight, _survival], Cards(rig));
    }

    [Fact]
    public async Task Choosing_another_game_clears_the_search_and_the_kind_choice()
    {
        var rig = await ShownAsync();
        rig.Screen.Search.Text = "survival";
        Choose(rig, HistoryKindFilter.Saves);

        rig.Shell.SelectGame(SampleScenario.SatisfactoryId);
        await rig.Screen.ReloadAsync();

        Assert.Equal(string.Empty, rig.Screen.Search.Text);
        Assert.Equal(HistoryKindFilter.All, rig.Screen.SelectedKindOption?.Filter);
        Assert.True(rig.Screen.Search.Query.IsEmpty);
    }

    [Fact]
    public async Task The_count_left_is_said_once_typing_pauses()
    {
        var rig = await ShownAsync();

        rig.Screen.Search.Text = "black";
        rig.Screen.Search.Text = "blackreach";
        Assert.Equal(string.Empty, rig.Screen.Search.Announcement);
        _clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Equal("1 of 8 snapshots", rig.Screen.Search.Announcement);
    }

    [Fact]
    public async Task While_recently_removed_is_closed_its_matches_are_counted_apart_from_the_snapshots_listed()
    {
        var rig = await ShownAsync(WithRemoved());

        rig.Screen.Search.Text = "blackreach";
        _clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Equal("1 of 8 snapshots, plus 1 of 2 in Recently removed", rig.Screen.Search.Announcement);
    }

    [Fact]
    public async Task While_recently_removed_is_open_its_matches_are_part_of_the_count()
    {
        var rig = await ShownAsync(WithRemoved());
        rig.Screen.IsRemovedOpen = true;

        rig.Screen.Search.Text = "blackreach";
        _clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Equal("2 of 10 snapshots", rig.Screen.Search.Announcement);
    }

    [Fact]
    public async Task A_game_with_only_recently_removed_snapshots_names_them_as_hidden_matches_while_the_section_is_closed()
    {
        var rig = await ShownOnlyRemovedAsync();

        rig.Screen.Search.Text = "blackreach";
        _clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Equal("1 of 1 snapshot in Recently removed", rig.Screen.Search.Announcement);
    }

    [Fact]
    public async Task A_game_with_only_recently_removed_snapshots_counts_them_in_the_list_once_the_section_is_open()
    {
        var rig = await ShownOnlyRemovedAsync();
        rig.Screen.IsRemovedOpen = true;

        rig.Screen.Search.Text = "blackreach";
        _clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Equal("1 of 1 snapshot", rig.Screen.Search.Announcement);
    }

    [Fact]
    public async Task A_closed_section_whose_snapshots_all_miss_the_search_adds_nothing_to_the_count()
    {
        var rig = await ShownAsync(WithRemoved());

        rig.Screen.Search.Text = "nolvus";
        _clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Empty(rig.Screen.Removed);
        Assert.Equal("1 of 8 snapshots", rig.Screen.Search.Announcement);
    }

    private async Task<HistoryRig> ShownOnlyRemovedAsync()
    {
        var scenario = ScenarioBuilder.Sample().WithGame(new Savepoint.Core.Catalog.GameDefinition(Id: "bare", Name: "Bare", Install: new Savepoint.Core.Catalog.InstallLocation(@"D:\Bare")))
            .WithRemovedSnapshot("bare", RemovedSnapshotSamples.Replaced);
        var rig = await ShownAsync(new FakeBackend(scenario));
        rig.Shell.SelectGame("bare");
        await rig.Screen.ReloadAsync();
        return rig;
    }

    [Fact]
    public async Task Changing_the_kind_announces_the_new_count()
    {
        var rig = await ShownAsync();

        Choose(rig, HistoryKindFilter.Saves);
        _clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Equal("5 of 8 snapshots", rig.Screen.Search.Announcement);
    }

    [Fact]
    public async Task Thousands_of_snapshots_are_filtered_by_name_date_and_kind()
    {
        var scenario = ScenarioBuilder.Sample();
        var start = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.FromHours(-5));
        for (var i = 0; i < 3000; i++)
        {
            var kind = i % 3 == 0 ? HistoryPointKind.VersionAndMods : HistoryPointKind.SaveOnly;
            scenario.WithHistoryPoint(
                SampleScenario.SkyrimId,
                new HistoryPoint(new HistoryPointId($"bulk-{i}"), kind, $"Session {i:0000}", start.AddHours(i), SampleScenario.ModdedId, "Modded playthrough - 7-2-26", new InstallState("1.6.640", 212), null, null, false, false));
        }

        var rig = await ShownAsync(new FakeBackend(scenario));
        Assert.Equal(3008, Cards(rig).Count);

        rig.Screen.Search.Text = "session 0299";
        _clock.Advance(HistoryScreenViewModel.TypingPause);
        Assert.Equal(["Session 0299"], Cards(rig));

        rig.Screen.Search.Text = "2024-01-01";
        _clock.Advance(HistoryScreenViewModel.TypingPause);
        Assert.Equal(12, Cards(rig).Count);

        Choose(rig, HistoryKindFilter.GameAndMods);
        Assert.Equal(4, Cards(rig).Count);
    }

    private async Task<HistoryRig> ManySnapshotsAsync()
    {
        var scenario = ScenarioBuilder.Sample();
        for (var i = 0; i < HistoryScreenViewModel.InstantRows + 50; i++)
        {
            scenario.WithHistoryPoint(
                SampleScenario.SkyrimId,
                new HistoryPoint(new HistoryPointId($"bulk-{i}"), HistoryPointKind.SaveOnly, $"Session {i:0000}", ScenarioBuilder.DefaultNow.AddHours(-i - 1), SampleScenario.ModdedId, "Modded playthrough - 7-2-26", new InstallState("1.6.640", 212), null, null, false, false));
        }

        return await ShownAsync(new FakeBackend(scenario));
    }

    [Fact]
    public async Task A_long_history_is_narrowed_once_typing_pauses_not_after_every_letter()
    {
        var rig = await ManySnapshotsAsync();
        var all = Cards(rig).Count;

        rig.Screen.Search.Text = "s";
        rig.Screen.Search.Text = "se";
        rig.Screen.Search.Text = "session 0007";
        _clock.Advance(HistoryScreenViewModel.TypingPause - TimeSpan.FromMilliseconds(1));
        Assert.Equal(all, Cards(rig).Count);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(["Session 0007"], Cards(rig));
    }

    [Fact]
    public async Task Typing_on_in_a_long_history_starts_the_wait_again()
    {
        var rig = await ManySnapshotsAsync();
        var all = Cards(rig).Count;

        rig.Screen.Search.Text = "session";
        _clock.Advance(HistoryScreenViewModel.TypingPause - TimeSpan.FromMilliseconds(1));
        rig.Screen.Search.Text = "session 0007";
        _clock.Advance(HistoryScreenViewModel.TypingPause - TimeSpan.FromMilliseconds(1));
        Assert.Equal(all, Cards(rig).Count);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(["Session 0007"], Cards(rig));
    }

    [Fact]
    public async Task A_screen_that_is_let_go_does_not_narrow_its_list_when_a_typing_pause_ends()
    {
        var rig = await ManySnapshotsAsync();
        var all = Cards(rig).Count;
        rig.Screen.Search.Text = "session 0007";

        rig.Screen.Dispose();
        _clock.Advance(HistoryScreenViewModel.TypingPause);

        Assert.Equal(all, Cards(rig).Count);
    }

    [Fact]
    public async Task A_screen_that_is_let_go_says_no_count_when_the_announcement_pause_ends()
    {
        var rig = await ShownAsync();
        rig.Screen.Search.Text = "survival";

        rig.Screen.Dispose();
        _clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Equal(string.Empty, rig.Screen.Search.Announcement);
    }

    [Fact]
    public async Task Choosing_a_kind_in_a_long_history_narrows_at_once()
    {
        var rig = await ManySnapshotsAsync();

        Choose(rig, HistoryKindFilter.GameAndMods);

        Assert.Equal(4, Cards(rig).Count);
    }

    [Fact]
    public async Task The_count_of_a_long_history_search_is_said_after_the_list_has_narrowed()
    {
        var rig = await ManySnapshotsAsync();

        rig.Screen.Search.Text = "session 0007";
        _clock.Advance(HistoryScreenViewModel.TypingPause);
        Assert.Equal(string.Empty, rig.Screen.Search.Announcement);
        _clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Equal($"1 of {8 + HistoryScreenViewModel.InstantRows + 50} snapshots", rig.Screen.Search.Announcement);
    }
}
