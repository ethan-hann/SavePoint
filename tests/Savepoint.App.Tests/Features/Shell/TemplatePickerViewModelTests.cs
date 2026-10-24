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
