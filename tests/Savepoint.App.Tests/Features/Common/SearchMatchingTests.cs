using Savepoint.App.Features.Common.Search;

namespace Savepoint.App.Tests.Features.Common;

public class SearchMatchingTests
{
    private static bool Finds(string typed, string name) => SearchQuery.Parse(typed).Matches(SearchTarget.ForName(name));

    private static bool FindsIn(string typed, params string?[] fields) => SearchQuery.Parse(typed).Matches(SearchTarget.Of(fields));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_typed_matches_every_entry(string? typed)
    {
        var query = SearchQuery.Parse(typed);

        Assert.True(query.IsEmpty);
        Assert.True(query.Matches(SearchTarget.ForName("Skyrim Special Edition")));
    }

    [Fact]
    public void Typed_words_may_come_from_anywhere_in_the_name_in_any_order()
    {
        Assert.True(Finds("special", "Skyrim Special Edition"));
        Assert.True(Finds("edit skyrim", "Skyrim Special Edition"));
        Assert.False(Finds("fallout", "Skyrim Special Edition"));
    }

    [Fact]
    public void Every_typed_word_has_to_be_found()
    {
        Assert.False(Finds("skyrim fallout", "Skyrim Special Edition"));
    }

    [Fact]
    public void The_initials_of_a_name_count_so_skyrim_se_finds_skyrim_special_edition()
    {
        Assert.True(Finds("skyrim se", "Skyrim Special Edition"));
        Assert.False(Finds("skyrim xx", "Skyrim Special Edition"));
    }

    [Fact]
    public void Case_makes_no_difference_either_way()
    {
        Assert.True(Finds("SKYRIM", "skyrim special edition"));
        Assert.True(Finds("skyrim", "SKYRIM SPECIAL EDITION"));
    }

    [Fact]
    public void Accents_make_no_difference_either_way()
    {
        Assert.True(Finds("pokemon", "Pokémon Legends"));
        Assert.True(Finds("pokémon", "Pokemon Legends"));
        Assert.True(Finds("ÉCLAIR", "Eclair"));
    }

    [Fact]
    public void Punctuation_in_a_name_does_not_get_in_the_way()
    {
        Assert.True(Finds("spiderman", "Spider-Man"));
        Assert.True(Finds("spider-man", "Spider Man"));
        Assert.True(Finds("sid meiers civ", "Sid Meier's Civilization VI"));
        Assert.True(Finds("sid meier's", "Sid Meier’s Civilization VI"));
    }

    [Fact]
    public void A_search_over_several_fields_finds_a_word_in_any_of_them_but_not_across_two()
    {
        Assert.True(FindsIn("hard", "Modded playthrough", "Hardcore run, no fast travel"));
        Assert.True(FindsIn("modded fast", "Modded playthrough", "Hardcore run, no fast travel"));
        Assert.False(FindsIn("playthroughhard", "Modded playthrough", "Hardcore run"));
    }

    [Fact]
    public void Empty_and_missing_fields_are_skipped()
    {
        Assert.True(FindsIn("run", null, "", "Hardcore run"));
        Assert.False(FindsIn("run", null, ""));
    }

    [Fact]
    public void A_date_typed_with_dashes_is_found_in_an_entry_that_holds_it()
    {
        Assert.True(FindsIn("2026-09", "Cleared Blackreach", "2026-09-05 23:42 september"));
        Assert.False(FindsIn("2026-10", "Cleared Blackreach", "2026-09-05 23:42 september"));
    }
}
