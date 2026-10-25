using System.Text;
using Savepoint.Core.Artwork;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.Artwork;

// The shapes are those of the .info files GOG's installer wrote for a base game with two add-ons and for an older install.
public class GogProductIdDetectorTests
{
    private static string Info(string gameId, string? rootGameId) =>
        "{\n    \"clientId\": \"53652320436400298\",\n    \"gameId\": \"" + gameId + "\",\n    \"language\": \"English\",\n    \"name\": \"Some Game\",\n"
        + (rootGameId is null ? string.Empty : "    \"rootGameId\": \"" + rootGameId + "\",\n")
        + "    \"version\": 1\n}";

    private static void Write(TempFolder install, string gameId, string? rootGameId) =>
        install.WriteText($"goggame-{gameId}.info", Info(gameId, rootGameId));

    [Fact]
    public void The_base_game_is_named_even_when_an_add_ons_file_sorts_first()
    {
        using var install = new TempFolder();
        Write(install, "1256837418", "1423049311");
        Write(install, "1423049311", "1423049311");
        Write(install, "1597316373", "1423049311");

        Assert.Equal("1423049311", GogProductIdDetector.Detect(install.Path));
    }

    [Fact]
    public void An_older_file_without_a_root_id_names_its_own_game()
    {
        using var install = new TempFolder();
        Write(install, "1207658995", null);

        Assert.Equal("1207658995", GogProductIdDetector.Detect(install.Path));
    }

    [Fact]
    public void A_file_with_a_byte_order_mark_is_read()
    {
        using var install = new TempFolder();
        install.WriteBytesWith("goggame-1228964594.info", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Info("1228964594", null))]);

        Assert.Equal("1228964594", GogProductIdDetector.Detect(install.Path));
    }

    [Fact]
    public void A_file_that_is_not_json_is_skipped_for_one_that_is()
    {
        using var install = new TempFolder();
        install.WriteText("goggame-1111111111.info", "{ \"gameId\": \"11");
        Write(install, "1423049311", "1423049311");

        Assert.Equal("1423049311", GogProductIdDetector.Detect(install.Path));
    }

    [Theory]
    [InlineData("..\\..\\evil")]
    [InlineData("12/34")]
    [InlineData("")]
    [InlineData("abc")]
    public void An_id_that_is_not_all_digits_is_never_returned_since_it_becomes_a_folder_name(string badId)
    {
        using var install = new TempFolder();
        install.WriteText("goggame-9.info", Info(badId, badId));

        Assert.Null(GogProductIdDetector.Detect(install.Path));
    }

    [Fact]
    public void A_folder_with_no_info_file_has_no_id()
    {
        using var install = new TempFolder();
        install.WriteText("game.exe", "x");

        Assert.Null(GogProductIdDetector.Detect(install.Path));
    }

    [Fact]
    public void A_missing_or_empty_folder_has_no_id_and_throws_nothing()
    {
        Assert.Null(GogProductIdDetector.Detect(null));
        Assert.Null(GogProductIdDetector.Detect(" "));
        Assert.Null(GogProductIdDetector.Detect(Path.Combine(Path.GetTempPath(), "savepoint-gog-" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void An_info_file_far_larger_than_any_real_one_is_not_read()
    {
        using var install = new TempFolder();
        install.WriteText("goggame-1423049311.info", Info("1423049311", "1423049311") + new string(' ', 2 * 1024 * 1024));

        Assert.Null(GogProductIdDetector.Detect(install.Path));
    }
}
