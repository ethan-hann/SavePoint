using Savepoint.App.Localization;

namespace Savepoint.App.Tests.Localization;

public class LocalizationCatalogTests
{
    private static LocalizationCatalog CatalogOf(params (string Key, string Message)[] entries) =>
        new(entries.ToDictionary(e => e.Key, e => e.Message), "en", throwOnMissingKey: true);

    [Fact]
    public void Embedded_english_catalog_formats_a_plain_key()
    {
        var catalog = LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true);

        Assert.Equal("SavePoint", catalog.Format("app.name"));
        Assert.Equal("Keep the setup that works", catalog.Format("firstRun.title"));
    }

    [Fact]
    public void Embedded_catalog_merges_every_file_of_the_locale()
    {
        var catalog = LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true);

        Assert.Equal("SavePoint", catalog.Format("app.name"));
        Assert.Equal("Something went wrong. Try again.", catalog.Format("error.unknown"));
        Assert.Equal("Give the game a name.", catalog.Format("validation.game.nameRequired"));
        Assert.Equal("Keep the setup that works", catalog.Format("firstRun.title"));
    }

    [Fact]
    public void Embedded_locales_list_the_locale_folders()
    {
        Assert.Contains("en", LocalizationCatalog.EmbeddedLocales());
    }

    [Fact]
    public void Embedded_english_holds_exactly_the_keys_of_the_files_on_disk()
    {
        var onDisk = CatalogLint.English().Keys.Order(StringComparer.Ordinal).ToList();
        var embedded = LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true).Keys.Order(StringComparer.Ordinal).ToList();

        Assert.Equal(onDisk, embedded);
    }

    [Fact]
    public void Files_of_one_locale_are_merged()
    {
        var catalog = LocalizationCatalog.FromFiles(
            [("a.json", """{ "a.one": "One" }"""), ("b.json", """{ "b.two": "Two" }""")],
            "en",
            throwOnMissingKey: true);

        Assert.Equal("One", catalog.Format("a.one"));
        Assert.Equal("Two", catalog.Format("b.two"));
    }

    [Fact]
    public void A_key_in_two_files_is_an_error_naming_the_key_and_both_files()
    {
        var error = Assert.Throws<InvalidDataException>(() => LocalizationCatalog.FromFiles(
            [("a.json", """{ "x.same": "One" }"""), ("b.json", """{ "x.same": "Two" }""")],
            "en"));

        Assert.Contains("x.same", error.Message);
        Assert.Contains("a.json", error.Message);
        Assert.Contains("b.json", error.Message);
    }

    [Fact]
    public void A_file_that_is_not_a_json_object_of_strings_is_an_error_naming_the_file()
    {
        var error = Assert.ThrowsAny<Exception>(() => LocalizationCatalog.FromFiles([("broken.json", "[1, 2]")], "en"));

        Assert.Contains("broken.json", error.Message);
    }

    [Fact]
    public void Locales_can_carry_a_region()
    {
        var catalog = LocalizationCatalog.FromFiles([("a.json", """{ "a.one": "One" }""")], "en-GB", throwOnMissingKey: true);

        Assert.Equal("en-GB", catalog.Locale);
        Assert.Equal("One", catalog.Format("a.one"));
    }

    [Theory]
    [InlineData(1, "1 save")]
    [InlineData(3, "3 saves")]
    public void Plural_picks_the_form_for_the_count(int count, string expected)
    {
        var catalog = CatalogOf(("test.saves", "{count, plural, one {# save} other {# saves}}"));

        var text = catalog.Format("test.saves", new Dictionary<string, object?> { ["count"] = count });

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData("windows", "Match Windows")]
    [InlineData("linux", "Match your system")]
    public void Select_picks_the_branch_for_the_value(string os, string expected)
    {
        var catalog = CatalogOf(("test.match", "{os, select, windows {Match Windows} other {Match your system}}"));

        var text = catalog.Format("test.match", new Dictionary<string, object?> { ["os"] = os });

        Assert.Equal(expected, text);
    }

    [Fact]
    public void Plain_placeholder_is_replaced_with_the_argument()
    {
        var catalog = CatalogOf(("test.greeting", "Hello, {name}"));

        var text = catalog.Format("test.greeting", new Dictionary<string, object?> { ["name"] = "Ada" });

        Assert.Equal("Hello, Ada", text);
    }

    [Fact]
    public void Missing_key_throws_and_names_the_key()
    {
        var catalog = CatalogOf(("test.present", "here"));

        var error = Assert.Throws<KeyNotFoundException>(() => catalog.Format("test.absent"));

        Assert.Contains("test.absent", error.Message);
    }

    [Fact]
    public void Missing_key_falls_back_to_the_key_when_throwing_is_off()
    {
        var catalog = new LocalizationCatalog(new Dictionary<string, string>(), "en", throwOnMissingKey: false);

        Assert.Equal("test.absent", catalog.Format("test.absent"));
    }

    [Fact]
    public void Unknown_locale_throws()
    {
        Assert.Throws<FileNotFoundException>(() => LocalizationCatalog.LoadEmbedded("zz"));
    }
}
