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
        Assert.Equal("Keep the setup that works", catalog.Format("app.placeholder.heading"));
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
