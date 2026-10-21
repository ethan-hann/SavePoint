using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using static Savepoint.Core.Tests.Catalog.CatalogSamples;

namespace Savepoint.Core.Tests.Catalog;

public class SettingsValidationTests
{
    [Fact]
    public void Default_settings_have_no_issues()
    {
        Assert.Empty(CatalogValidator.Validate(Settings()));
    }

    [Theory]
    [InlineData(ThemePreference.System)]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public void Every_theme_is_accepted(ThemePreference theme)
    {
        Assert.Empty(CatalogValidator.Validate(Settings() with { Theme = theme }));
    }

    [Theory]
    [InlineData(AccentColorId.Blue)]
    [InlineData(AccentColorId.Violet)]
    [InlineData(AccentColorId.Green)]
    [InlineData(AccentColorId.Amber)]
    [InlineData(AccentColorId.Crimson)]
    public void Every_accent_color_is_accepted(AccentColorId color)
    {
        Assert.Empty(CatalogValidator.Validate(Settings() with { AccentColorId = color }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_storage_root_is_reported(string root)
    {
        var issues = CatalogValidator.Validate(Settings(root));

        Assert.Equal([Issue("storageRoot", ValidationMessageIds.SettingsStorageRootRequired)], issues);
    }

    [Fact]
    public void Unknown_theme_is_reported()
    {
        var issues = CatalogValidator.Validate(Settings() with { Theme = (ThemePreference)9 });

        Assert.Equal([Issue("theme", ValidationMessageIds.SettingsThemeInvalid)], issues);
    }

    [Fact]
    public void Unknown_accent_color_is_reported()
    {
        var issues = CatalogValidator.Validate(Settings() with { AccentColorId = (AccentColorId)9 });

        Assert.Equal([Issue("accentColorId", ValidationMessageIds.SettingsAccentColorInvalid)], issues);
    }

    [Fact]
    public void Unknown_layout_is_reported()
    {
        var issues = CatalogValidator.Validate(Settings() with { Layout = (ShellLayout)9 });

        Assert.Equal([Issue("layout", ValidationMessageIds.SettingsLayoutInvalid)], issues);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("EN")]
    [InlineData("")]
    public void Locale_other_than_en_is_reported(string locale)
    {
        var issues = CatalogValidator.Validate(Settings() with { Locale = locale });

        Assert.Equal([Issue("locale", ValidationMessageIds.SettingsLocaleUnsupported)], issues);
    }

    [Fact]
    public void Every_problem_is_reported_at_once_in_field_order()
    {
        var settings = new AppSettings(
            StorageRoot: " ",
            Theme: (ThemePreference)9,
            AccentColorId: (AccentColorId)9,
            Locale: "de",
            Layout: (ShellLayout)9);

        Assert.Equal(
            [
                Issue("storageRoot", ValidationMessageIds.SettingsStorageRootRequired),
                Issue("theme", ValidationMessageIds.SettingsThemeInvalid),
                Issue("accentColorId", ValidationMessageIds.SettingsAccentColorInvalid),
                Issue("locale", ValidationMessageIds.SettingsLocaleUnsupported),
                Issue("layout", ValidationMessageIds.SettingsLayoutInvalid),
            ],
            CatalogValidator.Validate(settings));
    }

    [Fact]
    public void Normalize_trims_the_storage_root()
    {
        var normalized = CatalogValidator.Normalize(Settings("  /data/savepoint  "));

        Assert.Equal("/data/savepoint", normalized.StorageRoot);
    }

    [Fact]
    public void RequireValid_returns_the_normalized_settings()
    {
        var result = CatalogValidator.RequireValid(Settings(" /data "));

        Assert.Equal("/data", result.StorageRoot);
    }

    [Fact]
    public void RequireValid_throws_with_the_issues_for_invalid_settings()
    {
        var error = Assert.Throws<ValidationException>(() => CatalogValidator.RequireValid(Settings("")));

        Assert.Equal(
            [Issue("storageRoot", ValidationMessageIds.SettingsStorageRootRequired)],
            (IReadOnlyList<ValidationIssue>)error.Args["issues"]!);
    }

    [Fact]
    public void Message_ids_are_unique_and_live_under_the_validation_namespace()
    {
        var ids = typeof(ValidationMessageIds)
            .GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches(@"^validation\.(game|launch|profile|settings)\.[a-z][A-Za-z]+$", id));
    }
}
