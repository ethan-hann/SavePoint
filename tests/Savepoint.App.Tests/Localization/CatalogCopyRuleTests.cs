namespace Savepoint.App.Tests.Localization;

// The copy style rules (one dash style, helper lines end with a period, American spelling), each shown on a bad sample so a rule that stops catching anything fails here and not only in the repository scan.
public class CatalogCopyRuleTests
{
    private static IReadOnlyDictionary<string, string> Catalog(params (string Key, string Message)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Message);

    [Theory]
    [InlineData("Nothing is lost - your saves stay on disk.")]
    [InlineData("Nothing is lost -- your saves stay on disk.")]
    [InlineData("Nothing is lost \u2013 your saves stay on disk.")]
    [InlineData("{count} - {name}")]
    public void A_spaced_hyphen_or_en_dash_in_a_value_is_reported_with_its_key(string message)
    {
        var problem = Assert.Single(CatalogLint.SpacedHyphens(Catalog(("test.sample", message))));

        Assert.Contains("test.sample", problem);
    }

    [Theory]
    [InlineData("Nothing is lost \u2014 your saves stay on disk.")]
    [InlineData("A well-known, 7-day, C:\\Games\\Mod-Pack folder.")]
    [InlineData("Mods from 2020–2024 work.")]
    public void An_em_dash_and_a_hyphen_inside_a_word_are_allowed(string message)
    {
        Assert.Empty(CatalogLint.SpacedHyphens(Catalog(("test.sample", message))));
    }

    [Fact]
    public void A_default_name_keeps_its_spaced_hyphen_because_it_becomes_a_profile_name()
    {
        var catalog = Catalog(("launch.keep.defaultName", "Kept changes - {date}"), ("profiles.wizard.defaultName", "Modded playthrough - {date}"));

        Assert.Empty(CatalogLint.SpacedHyphens(catalog));
    }

    [Theory]
    [InlineData("gameEditor.processName.helper", "Used when the launcher closes itself")]
    [InlineData("games.editProfile.versionHelperDetected", "Leave blank to use the detected version, {detectedVersion}")]
    public void A_helper_line_without_a_final_period_is_reported_with_its_key(string key, string message)
    {
        var problem = Assert.Single(CatalogLint.HelperLinesWithoutPeriod(Catalog((key, message))));

        Assert.Contains(key, problem);
    }

    [Fact]
    public void Helper_lines_that_end_with_a_period_and_other_lines_without_one_are_allowed()
    {
        var catalog = Catalog(
            ("gameEditor.processName.helper", "Used when the launcher closes itself."),
            ("settings.layout.sidebarHint", "Every game one click away"),
            ("games.header.editGame", "Edit game"));

        Assert.Empty(CatalogLint.HelperLinesWithoutPeriod(catalog));
    }

    [Theory]
    [InlineData("Pick a colour.", "colour")]
    [InlineData("Optimise storage", "Optimise")]
    [InlineData("Saving was cancelled.", "cancelled")]
    [InlineData("Cancelling the switch.", "Cancelling")]
    [InlineData("SavePoint recognises it.", "recognises")]
    [InlineData("Centred on the screen.", "Centred")]
    [InlineData("The grey area.", "grey")]
    [InlineData("Open the dialogue.", "dialogue")]
    [InlineData("Your behaviour.", "behaviour")]
    [InlineData("An unrecognised file.", "unrecognised")]
    [InlineData("The tile is greyed out.", "greyed")]
    [InlineData("A colourful tile.", "colourful")]
    [InlineData("The behavioural change.", "behavioural")]
    [InlineData("Protective armour.", "armour")]
    [InlineData("Localise the text.", "Localise")]
    [InlineData("The organisational chart.", "organisational")]
    [InlineData("A greying screen.", "greying")]
    public void A_non_american_spelling_in_a_value_is_reported_with_its_key_and_the_word(string message, string word)
    {
        var problem = Assert.Single(CatalogLint.NonAmericanSpellings(Catalog(("test.sample", message))));

        Assert.Contains("test.sample", problem);
        Assert.Contains(word, problem);
    }

    [Theory]
    [InlineData("Pick a color.")]
    [InlineData("Optimize storage")]
    [InlineData("Saving was canceled.")]
    [InlineData("Canceling the switch.")]
    [InlineData("That is an organism.")]
    [InlineData("A realistic view of the center.")]
    [InlineData("The analyses are done.")]
    [InlineData("They fulfill and enroll.")]
    [InlineData("A greyhound is a dog.")]
    public void American_spellings_and_words_that_only_contain_a_british_stem_are_allowed(string message)
    {
        Assert.Empty(CatalogLint.NonAmericanSpellings(Catalog(("test.sample", message))));
    }

    [Fact]
    public void A_british_spelling_in_a_key_is_allowed()
    {
        Assert.Empty(CatalogLint.NonAmericanSpellings(Catalog(("error.operation.cancelled", "That was canceled."))));
    }
}
