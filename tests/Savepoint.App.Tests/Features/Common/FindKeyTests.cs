using Avalonia.Input;
using Savepoint.App.Features.Common.Search;

namespace Savepoint.App.Tests.Features.Common;

public class FindKeyTests
{
    [Fact]
    public void Ctrl_Shift_F_asks_for_the_games_search_off_macOS()
    {
        Assert.True(FindKey.MatchesGames(Key.F, KeyModifiers.Control | KeyModifiers.Shift, isMacOS: false));
    }

    [Fact]
    public void Cmd_Shift_F_asks_for_the_games_search_on_macOS()
    {
        Assert.True(FindKey.MatchesGames(Key.F, KeyModifiers.Meta | KeyModifiers.Shift, isMacOS: true));
    }

    [Theory]
    [InlineData(false, KeyModifiers.Control)]
    [InlineData(false, KeyModifiers.Shift)]
    [InlineData(false, KeyModifiers.Meta | KeyModifiers.Shift)]
    [InlineData(false, KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt)]
    [InlineData(true, KeyModifiers.Meta)]
    [InlineData(true, KeyModifiers.Control | KeyModifiers.Shift)]
    public void Other_modifiers_do_not_ask_for_the_games_search(bool isMacOS, KeyModifiers modifiers)
    {
        Assert.False(FindKey.MatchesGames(Key.F, modifiers, isMacOS));
    }

    [Fact]
    public void Another_letter_does_not_ask_for_the_games_search()
    {
        Assert.False(FindKey.MatchesGames(Key.G, KeyModifiers.Control | KeyModifiers.Shift, isMacOS: false));
    }

    [Fact]
    public void The_find_key_alone_does_not_take_the_shifted_form()
    {
        Assert.True(FindKey.Matches(Key.F, KeyModifiers.Control, isMacOS: false));
        Assert.False(FindKey.Matches(Key.F, KeyModifiers.Control | KeyModifiers.Shift, isMacOS: false));
    }
}
