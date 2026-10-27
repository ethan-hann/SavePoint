using System.Text;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Paths;

// The marker in a mods folder names its role "mods"; any other word is still not a role.
public class ConsentMarkerModsTests
{
    private static readonly DateTimeOffset _confirmedAt = new(2026, 10, 20, 9, 3, 11, 402, TimeSpan.FromHours(-5));

    [Fact]
    public void A_mods_marker_is_written_with_the_role_mods_and_read_back()
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");

        ConsentMarker.Write(work, "game", RepoRole.Mods, _confirmedAt);

        Assert.Equal(
            "{\n  \"gameId\": \"game\",\n  \"role\": \"mods\",\n  \"confirmedAt\": \"2026-10-20T14:03:11.402Z\"\n}",
            File.ReadAllText(Path.Combine(work, ConsentMarker.FileName), Encoding.UTF8));
        var state = Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(work));
        Assert.Equal(("game", RepoRole.Mods), (state.Marker.GameId, state.Marker.Role));
    }

    [Theory]
    [InlineData("mod")]
    [InlineData("Mods")]
    [InlineData("MODS")]
    [InlineData("modded")]
    public void A_marker_with_another_spelling_of_the_role_is_corrupt(string role)
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");
        File.WriteAllText(Path.Combine(work, ConsentMarker.FileName), $"{{\"gameId\":\"a\",\"role\":\"{role}\",\"confirmedAt\":\"2026-10-20T14:03:11.402Z\"}}");

        Assert.IsType<ConsentMarkerState.Corrupt>(ConsentMarker.Read(work));
    }

    [Theory]
    [InlineData(RepoRole.Install)]
    [InlineData(RepoRole.External)]
    public void A_mods_folder_cannot_take_over_a_folder_marked_for_another_role(RepoRole existing)
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");
        ConsentMarker.Write(work, "game", existing, _confirmedAt);

        var error = Assert.Throws<ConsentMarkerException>(() => ConsentMarker.Write(work, "game", RepoRole.Mods, _confirmedAt));

        Assert.Equal("error.consentMarker.conflict", error.MessageId);
        Assert.Equal(existing, error.Args["existingRole"]);
    }

    [Fact]
    public void Verify_passes_for_the_mods_role_and_refuses_the_saves_role_on_the_same_folder()
    {
        using var tree = new TempTree();
        var work = tree.Dir("work");
        ConsentMarker.Write(work, "game", RepoRole.Mods, _confirmedAt);

        ConsentMarker.Verify(work, "game", RepoRole.Mods);
        var error = Assert.Throws<ConsentMarkerException>(() => ConsentMarker.Verify(work, "game", RepoRole.External));

        Assert.Equal("error.consentMarker.conflict", error.MessageId);
    }
}
