namespace Savepoint.App.Tests.Localization;

// The UI always supplies `os` (windows, macos or linux) when it formats a message, so messages may use {os, select, ...}.
// Catalog keys are never composed at run time ("error." + x, $"error.{x}"); the unused-key scan only sees literal keys.
public class CatalogLintTests
{
    [Fact]
    public void English_catalog_has_no_banned_vocabulary()
    {
        LintAssert.NoProblems(CatalogLint.BannedVocabulary(CatalogLint.English()));
    }

    [Fact]
    public void English_catalog_uses_none_of_the_retired_terms_outside_the_allowances()
    {
        LintAssert.NoProblems(CatalogLint.RetiredTerms(CatalogLint.English(), CatalogLint.AllowedRetiredTerms));
    }

    [Fact]
    public void Every_allowance_for_a_retired_term_is_still_needed()
    {
        LintAssert.NoProblems(CatalogLint.UnusedRetiredAllowances(CatalogLint.English(), CatalogLint.AllowedRetiredTerms));
    }

    [Fact]
    public void English_catalog_sets_off_asides_with_an_em_dash_never_a_spaced_hyphen()
    {
        LintAssert.NoProblems(CatalogLint.SpacedHyphens(CatalogLint.English()));
    }

    [Fact]
    public void English_catalog_holds_no_text_that_was_encoded_twice()
    {
        LintAssert.NoProblems(CatalogLint.DoubleEncodedText(CatalogLint.English()));
    }

    [Fact]
    public void Every_english_helper_line_ends_with_a_period()
    {
        LintAssert.NoProblems(CatalogLint.HelperLinesWithoutPeriod(CatalogLint.English()));
    }

    [Fact]
    public void English_catalog_is_in_american_spelling()
    {
        LintAssert.NoProblems(CatalogLint.NonAmericanSpellings(CatalogLint.English()));
    }

    [Fact]
    public void Every_english_message_parses_and_formats()
    {
        LintAssert.NoProblems(CatalogLint.FormatProblems("en", CatalogLint.English()));
    }

    [Fact]
    public void Every_key_used_in_source_exists_in_the_catalog()
    {
        LintAssert.NoProblems(SourceLint.MissingKeys(SourceLint.CatalogReferences(), CatalogLint.English().Keys.ToList()));
    }

    [Fact]
    public void Every_catalog_key_is_used_in_source()
    {
        LintAssert.NoProblems(SourceLint.UnusedKeys(CatalogLint.English().Keys.ToList(), SourceLint.UsedKeys()));
    }

    [Fact]
    public void Source_never_builds_catalog_keys_at_run_time()
    {
        var namespaces = CatalogLint.English().Keys
            .Select(key => key[..key.IndexOf('.', StringComparison.Ordinal)])
            .Concat(["error", "validation"])
            .Distinct()
            .ToList();

        var problems = SourceLint.SourceFiles(".cs")
            .SelectMany(path => SourceLint.DynamicKeyConstruction(SourceLint.RelativePath(path), File.ReadAllText(path), namespaces))
            .ToList();

        LintAssert.NoProblems(problems);
    }

    [Fact]
    public void Source_xaml_has_no_hardcoded_text()
    {
        var problems = SourceLint.SourceFiles(".axaml")
            .SelectMany(path => SourceLint.HardcodedXamlText(SourceLint.RelativePath(path), File.ReadAllText(path)))
            .ToList();

        LintAssert.NoProblems(problems);
    }

    [Fact]
    public void Lang_folder_holds_only_locale_folders_with_no_key_defined_twice()
    {
        LintAssert.NoProblems(CatalogLint.LayoutProblems(SourceLint.LangDirectory()));
    }

    [Fact]
    public void Additional_locale_files_match_english()
    {
        LintAssert.NoProblems(CatalogLint.AdditionalLocaleProblems(SourceLint.LangDirectory()));
    }
}
