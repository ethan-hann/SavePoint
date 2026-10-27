using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Search;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;

namespace Savepoint.App.Tests.Features.Games;

// The tooltips on the two search boxes name the key that reaches each one, in the words of the player's system.
public class GamesSearchShortcutTipTests
{
    private static UiFormatter Text(string os) => new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), new ManualTimeProvider(), os, TimeZoneInfo.Utc);

    private static SearchBoxViewModel Box(string os, string key) => new(Text(os), new ImmediateUiDispatcher(), new ManualTimeProvider(), "Search", Text(os).Message(key));

    [Fact]
    public async Task The_profile_search_names_Ctrl_F_in_its_tooltip()
    {
        var rig = await GamesRig.StartAsync();

        Assert.Equal("Search profiles (Ctrl+F)", rig.Screen.ProfileSearch.Tip);
        Assert.Equal("Ctrl+F", rig.Screen.ProfileSearch.Shortcut);
    }

    [Fact]
    public async Task The_games_search_names_Ctrl_Shift_F_in_its_tooltip()
    {
        var rig = await GamesRig.StartAsync();

        Assert.Equal("Search games (Ctrl+Shift+F)", rig.Shell.GameSearch.Tip);
        Assert.Equal("Ctrl+Shift+F", rig.Shell.GameSearch.Shortcut);
    }

    [Theory]
    [InlineData("windows", "Ctrl+F", "Ctrl+Shift+F")]
    [InlineData("linux", "Ctrl+F", "Ctrl+Shift+F")]
    [InlineData("macos", "Cmd+F", "Cmd+Shift+F")]
    public void The_keys_are_named_for_the_players_system(string os, string find, string findGames)
    {
        var text = Text(os);

        Assert.Equal(find, text.Message("search.shortcut.find"));
        Assert.Equal(findGames, text.Message("search.shortcut.findGames"));
    }

    [Fact]
    public void A_box_with_a_key_has_a_tooltip_made_of_its_name_and_the_key()
    {
        var box = Box("macos", "search.shortcut.findGames");

        Assert.Equal("Cmd+Shift+F", box.Shortcut);
        Assert.Equal("Search (Cmd+Shift+F)", box.Tip);
    }

    [Fact]
    public void A_box_no_key_moves_to_has_no_tooltip()
    {
        var box = new SearchBoxViewModel(Text("windows"), new ImmediateUiDispatcher(), new ManualTimeProvider(), "Search snapshots");

        Assert.Null(box.Shortcut);
        Assert.Null(box.Tip);
    }
}
