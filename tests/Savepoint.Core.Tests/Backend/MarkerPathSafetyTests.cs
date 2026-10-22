using Savepoint.Core.Catalog;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Backend;

// A game id read from a marker file never becomes a path outside the storage root (D31).
public class MarkerPathSafetyTests
{
    // The JSON text of the id \\probe-host\share, as it would sit inside a marker file.
    private const string _unc = "\\\\\\\\probe-host\\\\share";

    public static TheoryData<string> UnsafeIds => new()
    {
        _unc,
        "..",
        ".",
        "a/b",
        "a\\\\b",
        "C:\\\\somewhere",
        "-leading-dash",
        "has space",
    };

    private static string MarkerWith(string escapedId) =>
        "{\"gameId\":\"" + escapedId + "\",\"role\":\"install\",\"confirmedAt\":\"2026-10-20T14:03:11.402Z\"}";

    [Theory]
    [MemberData(nameof(UnsafeIds))]
    public async Task A_marker_whose_game_id_is_not_a_catalog_id_reads_as_corrupt(string escapedId)
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame();
        await File.WriteAllTextAsync(Path.Combine(game.Install.Path, ConsentMarker.FileName), MarkerWith(escapedId), workspace.Token);

        Assert.IsType<ConsentMarkerState.Corrupt>(ConsentMarker.Read(game.Install.Path));
    }

    [Fact]
    public async Task A_folder_carrying_a_crafted_marker_is_refused_and_the_marker_is_left_for_the_user_to_see()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame();
        var marker = Path.Combine(game.Install.Path, ConsentMarker.FileName);
        await File.WriteAllTextAsync(marker, MarkerWith(_unc), workspace.Token);

        var added = await workspace.Games.AddAsync(game);

        Assert.False(added.Ok);
        Assert.StartsWith("error.consentMarker.", added.Error!.MessageId, StringComparison.Ordinal);
        Assert.True(File.Exists(marker), "the planted file must not be deleted unseen");
    }

    [Theory]
    [InlineData("\\\\probe-host\\share")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("C:\\somewhere")]
    public async Task The_storage_paths_refuse_an_id_that_would_leave_the_storage_root(string id)
    {
        using var workspace = new BackendWorkspace();
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);

        Assert.Throws<ArgumentException>(() => storage.GameDirectory(id));
        Assert.Throws<ArgumentException>(() => storage.GitDirectory(id, RepoRole.Install));
    }

    [Fact]
    public async Task A_catalog_id_still_names_its_folder_under_the_root()
    {
        using var workspace = new BackendWorkspace();
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);

        Assert.Equal(Path.Combine(storage.Root, "skyrim-se", "install.git"), storage.GitDirectory("skyrim-se", RepoRole.Install));
    }
}
