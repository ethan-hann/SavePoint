using System.Text.Json;
using Savepoint.Core.Catalog;
using static Savepoint.Core.Tests.Catalog.CatalogSamples;

namespace Savepoint.Core.Tests.Catalog;

// The two catalog fields artwork adds: the game's Steam app id and the online-lookup setting.
public class ArtworkSettingsModelTests
{
    [Fact]
    public void Two_definitions_that_differ_only_in_the_steam_app_id_are_not_equal()
    {
        var plain = ExeGame();
        var withId = plain with { SteamAppId = 489830 };

        Assert.NotEqual(plain, withId);
        Assert.False(plain.Equals(withId));
        Assert.NotEqual(withId, withId with { SteamAppId = 489831 });
    }

    [Fact]
    public void Two_definitions_that_differ_only_in_the_steam_app_id_hash_differently()
    {
        var plain = ExeGame();

        Assert.NotEqual(plain.GetHashCode(), (plain with { SteamAppId = 489830 }).GetHashCode());
        Assert.NotEqual((plain with { SteamAppId = 1 }).GetHashCode(), (plain with { SteamAppId = 2 }).GetHashCode());
    }

    [Fact]
    public void Definitions_with_the_same_steam_app_id_are_equal_and_hash_alike()
    {
        var left = ExeGame() with { SteamAppId = 489830 };
        var right = ExeGame() with { SteamAppId = 489830 };

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void The_steam_app_id_round_trips_through_json_and_is_left_out_when_there_is_none()
    {
        var withId = ExeGame() with { SteamAppId = 489830 };

        var written = JsonSerializer.Serialize(withId, CatalogJsonContext.Default.GameDefinition);
        var read = JsonSerializer.Deserialize(written, CatalogJsonContext.Default.GameDefinition)!;
        var withoutId = JsonSerializer.Serialize(ExeGame(), CatalogJsonContext.Default.GameDefinition);

        Assert.Contains("\"steamAppId\": 489830", written, StringComparison.Ordinal);
        Assert.Equal(489830, read.SteamAppId);
        Assert.DoesNotContain("steamAppId", withoutId, StringComparison.Ordinal);
    }

    [Fact]
    public void A_definition_without_a_steam_app_id_reads_it_as_null()
    {
        var read = JsonSerializer.Deserialize("""{ "id": "g", "name": "G" }""", CatalogJsonContext.Default.GameDefinition)!;

        Assert.Null(read.SteamAppId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void A_steam_app_id_of_zero_or_less_is_refused_with_its_message_id(int id)
    {
        var issues = CatalogValidator.Validate(ExeGame() with { SteamAppId = id });

        Assert.Equal([Issue("steamAppId", ValidationMessageIds.GameSteamAppIdInvalid)], issues);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(489830)]
    [InlineData(int.MaxValue)]
    public void A_positive_steam_app_id_is_accepted(int id)
    {
        Assert.Empty(CatalogValidator.Validate(ExeGame() with { SteamAppId = id }));
    }

    [Fact]
    public void A_definition_with_no_steam_app_id_is_accepted_and_normalizing_keeps_the_id()
    {
        Assert.Empty(CatalogValidator.Validate(ExeGame()));
        Assert.Equal(7, CatalogValidator.Normalize(ExeGame() with { SteamAppId = 7 }).SteamAppId);
    }

    [Fact]
    public void Artwork_lookup_online_is_on_by_default()
    {
        Assert.True(AppSettings.Defaults("/data").LookUpArtworkOnline);
        Assert.True(new AppSettings().LookUpArtworkOnline);
    }

    [Fact]
    public void A_settings_file_from_before_the_setting_existed_reads_it_as_on()
    {
        var read = JsonSerializer.Deserialize("""{ "storageRoot": "S", "theme": "dark" }""", CatalogJsonContext.Default.AppSettings)!;

        Assert.True(read.LookUpArtworkOnline);
    }

    [Fact]
    public void Turning_artwork_lookup_off_round_trips_through_json()
    {
        var off = AppSettings.Defaults("/data") with { LookUpArtworkOnline = false };

        var written = JsonSerializer.Serialize(off, CatalogJsonContext.Default.AppSettings);
        var read = JsonSerializer.Deserialize(written, CatalogJsonContext.Default.AppSettings)!;

        Assert.Contains("\"lookUpArtworkOnline\": false", written, StringComparison.Ordinal);
        Assert.False(read.LookUpArtworkOnline);
        Assert.Equal(off, read);
    }

    [Fact]
    public void Settings_that_differ_only_in_the_lookup_setting_are_not_equal()
    {
        var on = AppSettings.Defaults("/data");

        Assert.NotEqual(on, on with { LookUpArtworkOnline = false });
    }
}
