using Savepoint.App.Features.Common.Search;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Features.Library;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Shell;

public class TemplatePickerViewModelTests
{
    private static GameTemplate Template(string name) => new(
        name.ToLowerInvariant().Replace(' ', '-'),
        name,
        new GameDefinition(Id: name.ToLowerInvariant().Replace(' ', '-'), Name: name));

    private static TemplatePickerViewModel Picker(params string[] names) =>
        new([.. names.Select(Template)], LibraryTestData.Text);

    private static IReadOnlyList<string> Shown(TemplatePickerViewModel picker) => [.. picker.Templates.Select(item => item.Name)];

    private static readonly string[] _names =
    [
        "The Witcher 3: Wild Hunt",
        "The Witcher 2: Assassins of Kings",
        "Sid Meier's Civilization VI",
        "Stardew Valley",
        "Hollow Knight",
    ];

    [Fact]
    public void With_no_search_every_template_is_listed_in_the_given_order()
    {
        var picker = Picker(_names);

        Assert.Equal(_names, Shown(picker));
        Assert.False(picker.ShowNoMatches);
    }

    [Fact]
    public void Searching_keeps_only_the_templates_whose_name_has_every_word_ignoring_case()
    {
        var picker = Picker(_names);

        picker.SearchText = "WITCHER";
        Assert.Equal(["The Witcher 3: Wild Hunt", "The Witcher 2: Assassins of Kings"], Shown(picker));

        picker.SearchText = "witcher 3";
        Assert.Equal(["The Witcher 3: Wild Hunt"], Shown(picker));

        picker.SearchText = "  wild   witcher ";
        Assert.Equal(["The Witcher 3: Wild Hunt"], Shown(picker));
    }

    [Fact]
    public void Punctuation_in_a_name_does_not_get_in_the_way_of_a_search()
    {
        var picker = Picker(_names);

        picker.SearchText = "sid meiers civ";

        Assert.Equal(["Sid Meier's Civilization VI"], Shown(picker));
    }

    [Theory]
    [InlineData("stalker", "S.T.A.L.K.E.R.: Call of Pripyat")]
    [InlineData("spiderman", "Marvel's Spider-Man Remastered")]
    [InlineData("halflife", "Half-Life 2")]
    [InlineData("half life 2", "Half-Life 2")]
    [InlineData("spider man", "Marvel's Spider-Man Remastered")]
    public void A_word_typed_without_the_punctuation_still_finds_the_game(string typed, string expected)
    {
        var picker = Picker("S.T.A.L.K.E.R.: Call of Pripyat", "Marvel's Spider-Man Remastered", "Half-Life 2", "Stardew Valley");

        picker.SearchText = typed;

        Assert.Equal([expected], Shown(picker));
    }

    [Fact]
    public void A_typed_word_is_found_anywhere_in_the_name_like_in_the_games_search()
    {
        var picker = Picker("Half-Life 2", "Marvel's Spider-Man Remastered");

        picker.SearchText = "alflife";

        Assert.Equal(["Half-Life 2"], Shown(picker));
    }

    [Theory]
    [InlineData("pokemon")]
    [InlineData("POKÉMON")]
    [InlineData("pokémon legends")]
    public void Accents_make_no_difference_in_the_picker(string typed)
    {
        var picker = Picker("Pokémon Legends: Arceus", "Stardew Valley");

        picker.SearchText = typed;

        Assert.Equal(["Pokémon Legends: Arceus"], Shown(picker));
    }

    [Fact]
    public void A_typed_accent_finds_a_name_without_it()
    {
        var picker = Picker("Pokemon Legends: Arceus", "Stardew Valley");

        picker.SearchText = "pokémon";

        Assert.Equal(["Pokemon Legends: Arceus"], Shown(picker));
    }

    [Fact]
    public void The_initials_of_a_name_count_in_the_picker()
    {
        var picker = Picker("The Elder Scrolls V: Skyrim Special Edition", "Skyrim", "Stardew Valley");

        picker.SearchText = "skyrim se";

        Assert.Equal(["The Elder Scrolls V: Skyrim Special Edition"], Shown(picker));
    }

    [Fact]
    public void Initials_alone_find_the_name_they_spell()
    {
        var picker = Picker("Hollow Knight", "Stardew Valley", "Half-Life 2");

        picker.SearchText = "sv";

        Assert.Equal(["Stardew Valley"], Shown(picker));
    }

    [Fact]
    public void A_search_that_matches_only_through_initials_ranks_after_names_that_start_with_it()
    {
        var picker = Picker("Super Vampire Hunter", "Sv Racing", "Stardew Valley");

        picker.SearchText = "sv";

        Assert.Equal(["Sv Racing", "Super Vampire Hunter", "Stardew Valley"], Shown(picker));
    }

    [Fact]
    public void The_games_search_and_the_picker_find_the_same_games_for_the_same_text()
    {
        var picker = new TemplatePickerViewModel(GameTemplates.All, LibraryTestData.Text);
        string[] searches = ["skyrim se", "witcher", "half life", "halflife", "spiderman", "pokemon", "sv", "2077", "of the", "x-com", "fallout new", "dsp", "  Cyber  punk "];

        foreach (var typed in searches)
        {
            picker.SearchText = typed;
            var query = SearchQuery.Parse(typed);
            var expected = GameTemplates.All.Where(template => query.Matches(SearchTarget.ForName(template.DisplayName))).Select(template => template.DisplayName);

            Assert.Equal([.. expected.Order(StringComparer.Ordinal)], [.. Shown(picker).Order(StringComparer.Ordinal)]);
        }
    }

    [Fact]
    public void Matches_keep_the_given_order_among_equally_good_fits()
    {
        var picker = Picker("Zed Game Alpha", "Alpha Game Zed", "Game Zed Alpha");

        picker.SearchText = "zed alpha";

        // Only the first holds both words with the first word leading; the others rank by their start, ties keep list order.
        Assert.Equal(["Zed Game Alpha", "Alpha Game Zed", "Game Zed Alpha"], Shown(picker));
    }

    [Fact]
    public void A_search_lists_names_that_start_with_it_before_names_that_only_hold_the_words()
    {
        var picker = Picker("Old Sid Civ", "Sid Meier's Civilization", "Sid Civilization Rebirth");

        picker.SearchText = "sid civ";

        // Whole-name start, then first-word start, then the given order.
        Assert.Equal(["Sid Civilization Rebirth", "Sid Meier's Civilization", "Old Sid Civ"], Shown(picker));
    }

    [Fact]
    public void A_picker_opened_with_a_search_starts_filtered_by_it()
    {
        var picker = new TemplatePickerViewModel([.. _names.Select(Template)], LibraryTestData.Text, "witcher 3");

        Assert.Equal("witcher 3", picker.SearchText);
        Assert.Equal(["The Witcher 3: Wild Hunt"], Shown(picker));
    }

    [Fact]
    public void Clearing_the_search_brings_the_whole_list_back()
    {
        var picker = Picker(_names);
        picker.SearchText = "hollow";

        picker.SearchText = string.Empty;

        Assert.Equal(_names, Shown(picker));
    }

    [Fact]
    public void A_search_nothing_matches_says_so_and_a_match_does_not()
    {
        var picker = Picker(_names);

        picker.SearchText = "zelda";

        Assert.Empty(picker.Templates);
        Assert.True(picker.ShowNoMatches);
        Assert.Equal("No games match your search.", picker.NoMatchesText);

        picker.SearchText = "stardew";
        Assert.False(picker.ShowNoMatches);
    }

    [Fact]
    public void Clearing_a_search_nothing_matches_lists_every_template_again()
    {
        var picker = Picker(_names);
        picker.SearchText = "zelda";

        picker.ClearSearchCommand.Execute(null);

        Assert.Equal(string.Empty, picker.SearchText);
        Assert.False(picker.ShowNoMatches);
        Assert.Equal(_names, Shown(picker));
        Assert.Equal("Clear search", picker.ClearSearchName);
    }

    [Fact]
    public void Three_hundred_templates_filter_to_the_one_asked_for()
    {
        var picker = new TemplatePickerViewModel(GameTemplates.All, LibraryTestData.Text);

        picker.SearchText = "skyrim special";

        Assert.True(GameTemplates.All.Count >= 300);
        Assert.Equal(["The Elder Scrolls V: Skyrim Special Edition"], Shown(picker));
    }

    [Fact]
    public async Task Choosing_a_filtered_template_answers_with_that_template()
    {
        var picker = Picker(_names);
        picker.SearchText = "hollow";

        picker.Templates.Single().Choose.Execute(null);

        var choice = Assert.IsType<TemplateChoice.Template>(await picker.Result);
        Assert.Equal("Hollow Knight", choice.Chosen.DisplayName);
    }
}
