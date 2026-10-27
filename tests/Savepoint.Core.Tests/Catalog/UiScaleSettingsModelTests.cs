using System.Text.Json;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Catalog;

// How large the player has asked the whole interface to be, kept as a percent on top of the operating system's own scaling.
public class UiScaleSettingsModelTests
{
    [Fact]
    public void The_interface_is_not_scaled_until_the_player_asks()
    {
        Assert.Equal(100, AppSettings.Defaults("/data").UiScalePercent);
        Assert.Equal(100, new AppSettings().UiScalePercent);
    }

    [Fact]
    public void A_settings_file_from_before_scaling_existed_reads_as_not_scaled()
    {
        var read = JsonSerializer.Deserialize("""{ "storageRoot": "S", "theme": "dark" }""", CatalogJsonContext.Default.AppSettings)!;

        Assert.Equal(100, read.UiScalePercent);
    }

    [Fact]
    public void A_chosen_scale_survives_a_round_trip_through_json_and_makes_the_settings_unequal()
    {
        var plain = AppSettings.Defaults("/data");
        var scaled = plain with { UiScalePercent = 150 };

        var written = JsonSerializer.Serialize(scaled, CatalogJsonContext.Default.AppSettings);
        var read = JsonSerializer.Deserialize(written, CatalogJsonContext.Default.AppSettings)!;

        Assert.Contains("\"uiScalePercent\": 150", written, StringComparison.Ordinal);
        Assert.Equal(150, read.UiScalePercent);
        Assert.Equal(scaled, read);
        Assert.NotEqual(plain, scaled);
    }
}
