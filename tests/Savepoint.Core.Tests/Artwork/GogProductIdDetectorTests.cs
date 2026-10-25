using System.Text;
using Savepoint.Core.Artwork;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.Artwork;

// The shapes are those of the .info files GOG's installer wrote for a base game with two add-ons and for an older install.
public class GogProductIdDetectorTests
{
    // Real base games list a play task; add-ons list none.
    private static string Info(string gameId, string? rootGameId, bool playTask = false) =>
        "{\n    \"clientId\": \"53652320436400298\",\n    \"gameId\": \"" + gameId + "\",\n    \"language\": \"English\",\n    \"name\": \"Some Game\",\n"
        + (playTask ? "    \"playTasks\": [{ \"category\": \"game\", \"isPrimary\": true, \"path\": \"game.exe\", \"type\": \"FileTask\" }],\n" : "    \"playTasks\": [],\n")
        + (rootGameId is null ? string.Empty : "    \"rootGameId\": \"" + rootGameId + "\",\n")
        + "    \"version\": 1\n}";

    private static void Write(TempFolder install, string gameId, string? rootGameId, bool playTask = false) =>
        install.WriteText($"goggame-{gameId}.info", Info(gameId, rootGameId, playTask));

    [Fact]
    public void The_root_named_by_most_files_wins_when_the_roots_differ()
    {
        using var install = new TempFolder();
        Write(install, "1000000001", "1000000009");
        Write(install, "1000000002", "1000000009");
        Write(install, "1000000003", "1000000003", playTask: true);

        Assert.Equal("1000000009", GogProductIdDetector.Detect(install.Path));
    }

    [Fact]
    public void In_a_tie_the_root_with_a_file_of_its_own_is_the_base_game_not_an_add_on()
    {
        using var install = new TempFolder();
        Write(install, "1000000002", "1000000005");
        Write(install, "1000000009", "1000000009", playTask: true);

        Assert.Equal("1000000009", GogProductIdDetector.Detect(install.Path));
    }

    [Fact]
    public void In_a_tie_between_older_files_the_one_with_play_tasks_is_the_base_game()
    {
        using var install = new TempFolder();
        Write(install, "1000000001", null);
        Write(install, "1000000009", null, playTask: true);

        Assert.Equal("1000000009", GogProductIdDetector.Detect(install.Path));
    }

    [Fact]
    public void In_a_tie_with_nothing_to_tell_them_apart_the_smallest_id_wins()
    {
        using var install = new TempFolder();
        Write(install, "1000000008", "1000000010");
        Write(install, "1000000009", "1000000007");

        Assert.Equal("1000000007", GogProductIdDetector.Detect(install.Path));
    }

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
