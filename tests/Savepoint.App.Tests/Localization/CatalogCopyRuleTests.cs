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

    private static RetiredAllowance Allow(string key, string term) => new(key, term, "a sample");

    [Theory]
    [InlineData("This point is old.", "point")]
    [InlineData("Both points exist.", "point")]
    [InlineData("You have unsaved changes.", "unsaved changes")]
    [InlineData("Half of it worked.", "half")]
    [InlineData("It was left alone.", "left alone")]
    [InlineData("Your storage folder.", "storage")]
    [InlineData("Optimize it.", "optimize")]
    [InlineData("Optimization ran.", "optimize")]
    [InlineData("Storage was optimised.", "optimize")]
    [InlineData("Capture it.", "capture")]
    [InlineData("It was captured.", "capture")]
    [InlineData("The marker file.", "marker file")]
    [InlineData("Ignore these.", "ignore")]
    [InlineData("Ignored files.", "ignore")]
    [InlineData("Files on disk.", "on disk")]
    public void A_retired_term_in_a_value_is_reported_with_its_key_and_the_term(string message, string term)
    {
        var problems = CatalogLint.RetiredTerms(Catalog(("test.sample", message)), []);

        Assert.Contains(problems, problem => problem.Contains("test.sample", StringComparison.Ordinal) && problem.Contains($"'{term}'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("SavePoint is open.")]
    [InlineData("\"{pointName}\" held it.")]
    [InlineData("A halfway house.")]
    [InlineData("The disk is full, and the storekeeper left.")]
    [InlineData("A snapshot of the saves folder.")]
    [InlineData("Free up space.")]
    public void Words_that_only_contain_a_retired_term_and_the_new_vocabulary_are_allowed(string message)
    {
        Assert.Empty(CatalogLint.RetiredTerms(Catalog(("test.sample", message)), []));
    }

    [Fact]
    public void A_retired_term_in_a_key_is_allowed()
    {
        Assert.Empty(CatalogLint.RetiredTerms(Catalog(("error.history.pointNotFound", "That snapshot is gone.")), []));
    }

    [Fact]
    public void An_allowance_lets_one_key_keep_one_term_and_nothing_else()
    {
        var catalog = Catalog(("a.one", "Point it at the folder."), ("a.two", "Point it at the folder."), ("a.three", "Point it at the storage."));
        var allowances = new[] { Allow("a.one", "point"), Allow("a.three", "point") };

        var problems = CatalogLint.RetiredTerms(catalog, allowances);

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, problem => problem.Contains("a.two", StringComparison.Ordinal) && problem.Contains("'point'", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("a.three", StringComparison.Ordinal) && problem.Contains("'storage'", StringComparison.Ordinal));
    }

    [Fact]
    public void An_allowance_that_ends_in_a_dot_covers_every_key_under_it()
    {
        var catalog = Catalog(("job.one", "Optimizing storage."), ("job.two", "Optimizing storage."), ("other.one", "Optimizing storage."));
        var allowances = new[] { Allow("job.", "optimize"), Allow("job.", "storage") };

        var problems = CatalogLint.RetiredTerms(catalog, allowances);

        Assert.Equal(2, problems.Count);
        Assert.All(problems, problem => Assert.Contains("other.one", problem, StringComparison.Ordinal));
    }

    [Fact]
    public void An_allowance_no_value_needs_any_more_is_reported_so_it_gets_removed()
    {
        var catalog = Catalog(("a.one", "Nothing retired here."), ("a.two", "Point it at the folder."), ("job.one", "Free up space."));
        var allowances = new[] { Allow("a.one", "point"), Allow("a.two", "point"), Allow("a.missing", "point"), Allow("job.", "storage") };

        var problems = CatalogLint.UnusedRetiredAllowances(catalog, allowances);

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, problem => problem.Contains("a.one", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("a.missing", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("job.", StringComparison.Ordinal));
    }

    // The samples are written with escapes, so this file never holds the garbled text itself.
    [Theory]
    [InlineData("{done} of {total} files Â· about {time} left")]
    [InlineData("Saved Ã© today")]
    [InlineData("Nothing is lost â\u0080\u0094 your saves stay on disk.")]
    [InlineData("Nothing is lost â€” your saves stay on disk.")]
    [InlineData("Savingâ€¦")]
    public void Text_that_was_encoded_twice_is_reported_with_its_key(string message)
    {
        var problem = Assert.Single(CatalogLint.DoubleEncodedText(Catalog(("test.sample", message))));

        Assert.Contains("test.sample", problem);
    }

    [Theory]
    [InlineData("{done} of {total} files · about {time} left")]
    [InlineData("Nothing is lost — your saves stay on disk.")]
    [InlineData("Saving…")]
    [InlineData("A café and a Façade.")]
    [InlineData("Â and Ã as capital letters, then letters.")]
    public void A_middle_dot_an_em_dash_an_accented_letter_and_a_stray_capital_are_not_double_encoded(string message)
    {
        Assert.Empty(CatalogLint.DoubleEncodedText(Catalog(("test.sample", message))));
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
