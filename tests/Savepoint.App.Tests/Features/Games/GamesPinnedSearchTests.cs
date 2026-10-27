using Savepoint.App.Features.Games;
using Savepoint.App.Tests.Fakes;

namespace Savepoint.App.Tests.Features.Games;

// The profile search stays above the table when there is room for it and the table, and scrolls with the content when there is not.
public class GamesPinnedSearchTests
{
    [Fact]
    public async Task Enough_room_pins_the_profile_search_above_the_table()
    {
        var rig = await GamesRig.StartAsync();

        rig.Screen.SetBodyHeight(GamesScreenViewModel.PinnedSearchMinBodyHeight);

        Assert.True(rig.Screen.ShowPinnedSearch);
        Assert.False(rig.Screen.ShowScrollingSearch);
    }

    [Fact]
    public async Task Too_little_room_scrolls_the_profile_search_with_the_profiles()
    {
        var rig = await GamesRig.StartAsync();

        rig.Screen.SetBodyHeight(GamesScreenViewModel.PinnedSearchMinBodyHeight - 1);

        Assert.False(rig.Screen.ShowPinnedSearch);
        Assert.True(rig.Screen.ShowScrollingSearch);
    }

    [Fact]
    public async Task Room_that_is_not_known_yet_pins_the_search()
    {
        var rig = await GamesRig.StartAsync();

        Assert.True(rig.Screen.ShowPinnedSearch);
        Assert.False(rig.Screen.ShowScrollingSearch);
    }

    [Fact]
    public async Task The_search_follows_the_room_as_it_changes()
    {
        var rig = await GamesRig.StartAsync();
        var changed = new List<string?>();
        rig.Screen.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        rig.Screen.SetBodyHeight(150);
        Assert.False(rig.Screen.ShowPinnedSearch);
        Assert.Contains(nameof(GamesScreenViewModel.ShowPinnedSearch), changed);
        Assert.Contains(nameof(GamesScreenViewModel.ShowScrollingSearch), changed);

        rig.Screen.SetBodyHeight(450);
        Assert.True(rig.Screen.ShowPinnedSearch);
        Assert.False(rig.Screen.ShowScrollingSearch);
    }

    [Fact]
    public async Task The_launch_cards_pane_height_does_not_move_the_search()
    {
        var rig = await GamesRig.StartAsync();
        rig.Screen.SetBodyHeight(GamesScreenViewModel.PinnedSearchMinBodyHeight);

        rig.Screen.SetPaneHeight(GamesScreenViewModel.PinnedCardMinHeight - 1);

        Assert.True(rig.Screen.ShowPinnedSearch);
    }

    [Fact]
    public async Task A_game_with_no_profiles_shows_no_search_in_either_place()
    {
        var rig = await GamesRig.StartAsync(new FakeBackend(ScenarioBuilder.Empty().WithSettings(SampleScenario.Settings).WithGame(SampleScenario.Caretaker)));

        rig.Screen.SetBodyHeight(450);
        Assert.False(rig.Screen.ShowPinnedSearch);
        Assert.False(rig.Screen.ShowScrollingSearch);

        rig.Screen.SetBodyHeight(150);
        Assert.False(rig.Screen.ShowPinnedSearch);
        Assert.False(rig.Screen.ShowScrollingSearch);
    }
}
