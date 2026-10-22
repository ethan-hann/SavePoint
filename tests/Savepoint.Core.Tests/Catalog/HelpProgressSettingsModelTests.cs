using System.Text.Json;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Catalog;

// What the in-app help remembers about a player: which of its three decision-point explanations it has already opened on its own.
public class HelpProgressSettingsModelTests
{
    [Fact]
    public void Nothing_has_been_explained_until_it_is_recorded()
    {
        var defaults = AppSettings.Defaults("/data");

        Assert.False(defaults.OlderChoiceHelpSeen);
        Assert.False(defaults.DiscardHelpSeen);
        Assert.False(defaults.ReplaceHelpSeen);
        Assert.False(new AppSettings().OlderChoiceHelpSeen);
    }

    [Fact]
    public void A_settings_file_from_before_the_help_existed_reads_every_explanation_as_not_yet_shown()
    {
        var read = JsonSerializer.Deserialize("""{ "storageRoot": "S", "theme": "dark", "lookUpArtworkOnline": false }""", CatalogJsonContext.Default.AppSettings)!;

        Assert.False(read.OlderChoiceHelpSeen);
        Assert.False(read.DiscardHelpSeen);
        Assert.False(read.ReplaceHelpSeen);
        Assert.Equal("S", read.StorageRoot);
        Assert.False(read.LookUpArtworkOnline);
    }

    [Fact]
    public void Each_explanation_is_remembered_on_its_own_and_survives_a_round_trip_through_json()
    {
        var shown = AppSettings.Defaults("/data") with { OlderChoiceHelpSeen = true, ReplaceHelpSeen = true };

        var written = JsonSerializer.Serialize(shown, CatalogJsonContext.Default.AppSettings);
        var read = JsonSerializer.Deserialize(written, CatalogJsonContext.Default.AppSettings)!;

        Assert.Contains("\"olderChoiceHelpSeen\": true", written, StringComparison.Ordinal);
        Assert.Contains("\"discardHelpSeen\": false", written, StringComparison.Ordinal);
        Assert.Contains("\"replaceHelpSeen\": true", written, StringComparison.Ordinal);
        Assert.True(read.OlderChoiceHelpSeen);
        Assert.False(read.DiscardHelpSeen);
        Assert.True(read.ReplaceHelpSeen);
        Assert.Equal(shown, read);
    }

    [Fact]
    public void Settings_that_differ_only_in_what_has_been_explained_are_not_equal()
    {
        var plain = AppSettings.Defaults("/data");

        Assert.NotEqual(plain, plain with { OlderChoiceHelpSeen = true });
        Assert.NotEqual(plain, plain with { DiscardHelpSeen = true });
        Assert.NotEqual(plain, plain with { ReplaceHelpSeen = true });
    }

    [Fact]
    public void The_tour_has_not_run_until_it_is_recorded_and_an_older_settings_file_reads_it_that_way()
    {
        var read = JsonSerializer.Deserialize("""{ "storageRoot": "S", "olderChoiceHelpSeen": true }""", CatalogJsonContext.Default.AppSettings)!;

        Assert.False(AppSettings.Defaults("/data").TourSeen);
        Assert.False(new AppSettings().TourSeen);
        Assert.False(read.TourSeen);
        Assert.True(read.OlderChoiceHelpSeen);
    }

    [Fact]
    public void That_the_tour_has_run_survives_a_round_trip_through_json_and_makes_the_settings_unequal()
    {
        var plain = AppSettings.Defaults("/data");
        var seen = plain with { TourSeen = true };

        var written = JsonSerializer.Serialize(seen, CatalogJsonContext.Default.AppSettings);
        var read = JsonSerializer.Deserialize(written, CatalogJsonContext.Default.AppSettings)!;

        Assert.Contains("\"tourSeen\": true", written, StringComparison.Ordinal);
        Assert.True(read.TourSeen);
        Assert.Equal(seen, read);
        Assert.NotEqual(plain, seen);
    }
}
