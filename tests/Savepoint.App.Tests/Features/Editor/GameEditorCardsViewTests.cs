using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.Editor;

// The editor groups its settings into cards in a fixed order, and each card says how much it matters.
public class GameEditorCardsViewTests
{
    private static readonly string[] _cardNames = ["GameCard", "SavesCard", "ModsCard", "ArtworkSection", "AdvancedCard"];

    private static double Top(EditorViewHarness harness, Control control) =>
        control.TranslatePoint(new Point(0, 0), harness.View)!.Value.Y;

    private static bool Inside(Control control, Control card) => control.GetVisualAncestors().Contains(card);

    private static string? TextOf(EditorViewHarness harness, string name) =>
        harness.Find<TextBlock>(name).Text;

    [AvaloniaFact]
    public async Task The_cards_are_drawn_in_the_order_game_saves_mods_artwork_then_More_settings()
    {
        using var harness = await EditorViewHarness.StartAsync(height: 1600);
        harness.Edit();
        harness.Settle();

        var tops = _cardNames.Select(name => Top(harness, harness.Find<Control>(name))).ToList();

        Assert.Equal(tops.Order(), tops);
        Assert.Equal(tops.Distinct().Count(), tops.Count);
    }

    [AvaloniaFact]
    public async Task Each_card_is_headed_and_says_how_much_it_matters()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();

        Assert.Equal("Game", TextOf(harness, "GameCardHeading"));
        Assert.Equal("Required", TextOf(harness, "GameCardTag"));
        Assert.Equal("Saves", TextOf(harness, "SavesCardHeading"));
        Assert.Equal("Recommended", TextOf(harness, "SavesCardTag"));
        Assert.Equal("Mods", TextOf(harness, "ModsCardHeading"));
        Assert.Equal("Optional", TextOf(harness, "ModsCardTag"));
        Assert.Equal("Artwork", TextOf(harness, "ArtworkCardHeading"));
        Assert.Equal("Optional", TextOf(harness, "ArtworkCardTag"));
    }

    [AvaloniaFact]
    public async Task Card_headings_are_headings_for_screen_readers()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();

        foreach (var heading in new[] { "GameCardHeading", "SavesCardHeading", "ModsCardHeading", "ArtworkCardHeading" })
        {
            Assert.Equal(2, AutomationProperties.GetHeadingLevel(harness.Find<TextBlock>(heading)));
        }
    }

    [AvaloniaFact]
    public async Task The_fields_a_game_cannot_work_without_are_all_in_the_required_card()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.New();
        var card = harness.Find<Border>("GameCard");

        foreach (var name in new[] { "NameField", "InstallField", "LaunchChoiceList", "ExeField" })
        {
            Assert.True(Inside(harness.Find<Control>(name), card), name);
        }

        Assert.False(Inside(harness.Find<Control>("SavesField"), card));
        Assert.False(Inside(harness.Find<Control>("ModsField"), card));
    }

    [AvaloniaFact]
    public async Task The_mods_folder_and_the_mod_files_share_the_mods_card()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.New();
        var card = harness.Find<Border>("ModsCard");

        foreach (var name in new[] { "ModsField", "BrowseModsButton", "ModsQuestion", "ModFoldersHost", "ModFoldersQuestion" })
        {
            Assert.True(Inside(harness.Find<Control>(name), card), name);
        }

        Assert.True(Inside(harness.Find<Control>("SavesField"), harness.Find<Border>("SavesCard")));
    }

    [AvaloniaFact]
    public async Task The_mods_card_labels_its_folder_outside_the_game_folder_and_its_list_inside_it()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.New();
        var card = harness.Find<Border>("ModsCard");

        var labels = card.GetVisualDescendants().OfType<TextBlock>().Where(block => block.Classes.Contains("overline")).Select(block => block.Text).ToList();

        Assert.Equal(["MODS OUTSIDE THE GAME FOLDER", "MOD FILES INSIDE THE GAME FOLDER"], labels);
    }

    [AvaloniaFact]
    public async Task Only_the_rules_and_extras_wait_behind_More_settings()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.New();
        var card = harness.Find<Border>("AdvancedCard");

        foreach (var name in new[] { "IgnoreRulesHost", "InstallIgnoreRulesHost", "LaunchArgsHost", "ProcessField", "SteamAppIdField", "RulesQuestion" })
        {
            Assert.True(Inside(harness.Find<Control>(name), card), name);
            Assert.False(harness.Find<Control>(name).IsEffectivelyVisible, name);
        }

        Assert.True(harness.Find<Control>("MoreSettingsToggle").IsEffectivelyVisible);
        Assert.True(harness.Find<Control>("ModFoldersHost").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_rules_lists_say_in_a_line_that_a_rule_is_a_pattern_and_not_a_regular_expression()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();

        Assert.Equal("Patterns such as **/*.log, not regular expressions.", TextOf(harness, "RulesSyntaxText"));
    }

    [AvaloniaTheory]
    [InlineData(640, 420)]
    [InlineData(910, 432)]
    public async Task Nothing_in_the_form_is_clipped_at_the_side_with_every_setting_open(int width, int height)
    {
        using var harness = await EditorViewHarness.StartAsync(width: width, height: height);
        harness.Edit();
        harness.Settle();
        var scroll = harness.Find<ScrollViewer>("FormScroll");

        // The form only scrolls up and down, so a control wider than the pane would be cut off rather than reachable.
        var clipped = scroll.GetVisualDescendants().OfType<Control>()
            .Where(control => control.IsEffectivelyVisible && control.Bounds.Width > 0 && control is TextBox or Button or Border)
            .Where(control => control.TranslatePoint(new Point(control.Bounds.Width, 0), scroll)!.Value.X > scroll.Bounds.Width + 0.5)
            .Select(control => control.Name ?? control.GetType().Name)
            .ToList();

        Assert.Empty(clipped);
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "The form should scroll up and down instead of clipping.");
    }

    [AvaloniaFact]
    public async Task Every_control_in_the_editor_has_a_name_for_screen_readers()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();
        harness.Settle();

        Assert.Empty(AutomationAudit.Problems(harness.View));
    }
}
