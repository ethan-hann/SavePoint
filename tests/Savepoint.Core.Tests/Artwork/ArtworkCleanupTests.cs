using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Tests.Backend;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Artwork;

// Where artwork lives and when it goes: with its game, with all data, and never with a storage move (D30).
public class ArtworkCleanupTests
{
    private static string ArtworkRoot(BackendWorkspace workspace) => Path.Combine(workspace.CatalogDirectory, "artwork");

    private static async Task<GameArtwork> ChooseImageAsync(BackendWorkspace workspace, GameDefinition game, string name = "mine.png")
    {
        var source = TestImages.Write(workspace.Root, name, TestImages.Png(64, 64));
        var set = await workspace.Backend.Artwork.SetCustomImageAsync(game.Id, source);
        Assert.True(set.Ok, set.Error?.Detail);
        return set.Value;
    }

    [Fact]
    public async Task Artwork_is_kept_in_the_configuration_folder_beside_the_catalog_not_in_the_storage_root()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();

        var art = await ChooseImageAsync(workspace, game);

        Assert.StartsWith(Path.Combine(workspace.CatalogDirectory, "artwork", game.Id) + Path.DirectorySeparatorChar, art.WidePath, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, "artwork")));
        Assert.DoesNotContain("artwork", Directory.EnumerateFileSystemEntries(Path.Combine(workspace.DefaultStorageRoot, game.Id), "*", SearchOption.AllDirectories).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Deleting_a_game_removes_its_artwork_and_only_its_own()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var other = await workspace.AddGameAsync(workspace.NewGame("Beta"));
        var gone = await ChooseImageAsync(workspace, game);
        var kept = await ChooseImageAsync(workspace, other);

        var deleted = await workspace.Games.DeleteAsync(game.Id);

        Assert.True(deleted.Ok, deleted.Error?.Detail);
        Assert.False(Directory.Exists(Path.Combine(ArtworkRoot(workspace), game.Id)));
        Assert.False(File.Exists(gone.WidePath));
        Assert.True(File.Exists(kept.WidePath));
    }

    [Fact]
    public async Task Deleting_a_game_that_never_had_artwork_is_fine()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();

        var deleted = await workspace.Games.DeleteAsync(game.Id);

        Assert.True(deleted.Ok, deleted.Error?.Detail);
        Assert.False(Directory.Exists(ArtworkRoot(workspace)));
    }

    [Fact]
    public async Task Clearing_all_data_removes_every_games_artwork_and_the_artwork_folder()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var other = await workspace.AddGameAsync(workspace.NewGame("Beta"));
        await ChooseImageAsync(workspace, game);
        await ChooseImageAsync(workspace, other);
        // Artwork of a game the catalog no longer lists goes too.
        BackendWorkspace.WriteFile(Path.Combine(ArtworkRoot(workspace), "forgotten-game", "custom.png"), "left over");

        var started = await workspace.Settings.ClearAllDataAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Empty(Assert.IsType<ClearAllDataResult>(job.Result).NotRemoved);
        Assert.False(Directory.Exists(ArtworkRoot(workspace)));
    }

    [Fact]
    public async Task Moving_the_storage_leaves_artwork_exactly_where_and_as_it_was()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var art = await ChooseImageAsync(workspace, game);
        var before = BackendWorkspace.Snapshot(ArtworkRoot(workspace));
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);
        var after = await workspace.Backend.Artwork.GetAsync(game.Id, workspace.Token);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(before, BackendWorkspace.Snapshot(ArtworkRoot(workspace)));
        Assert.Equal(art.WidePath, after.Value!.WidePath);
        Assert.Equal(ArtworkSource.Custom, after.Value.Source);
        Assert.DoesNotContain("artwork", Directory.EnumerateFileSystemEntries(destination, "*", SearchOption.AllDirectories).Select(Path.GetFileName));
    }
}
