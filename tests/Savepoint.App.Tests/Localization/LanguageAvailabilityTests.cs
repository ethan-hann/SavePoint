using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Localization;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Localization;

// SavePoint ships English only: Settings offers no language list, and a saved language that is not available says so without pointing at a list.
public class LanguageAvailabilityTests
{
    [Fact]
    public void The_locale_validation_message_says_the_language_is_not_available()
    {
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true));

        Assert.Equal("That language isn't available.", text.Message(ValidationMessageIds.SettingsLocaleInvalid));
    }

    [Fact]
    public void English_is_the_only_catalog_shipped_so_settings_has_no_language_list_to_fill()
    {
        // Adding a second catalog means Settings needs its language list (and the saved language applied) before this changes.
        Assert.Equal(["en"], LocalizationCatalog.EmbeddedLocales());
    }
}
