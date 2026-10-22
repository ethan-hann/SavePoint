using Savepoint.Core.Api;

namespace Savepoint.Core.Tests.Backend;

// A clear-all removes only git directories SavePoint made, and a move refuses what it cannot carry over, never skips it (D31).
public class StorageLookalikeFolderTests
{
    [Fact(Timeout = 120_000)]
    public async Task Clearing_all_data_leaves_a_folder_that_only_looks_like_a_game_folder_alone_and_lists_it()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync();
        var photos = Path.Combine(workspace.DefaultStorageRoot, "lookalike", "install.git", "family-photos.zip");
        var notes = Path.Combine(workspace.DefaultStorageRoot, "lookalike", "notes.txt");
        BackendWorkspace.WriteFile(photos, "somebody's files");
        BackendWorkspace.WriteFile(notes, "also somebody's");

        var started = await workspace.Settings.ClearAllDataAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId, TimeSpan.FromSeconds(120));

        Assert.Equal("somebody's files", await File.ReadAllTextAsync(photos, workspace.Token));
        Assert.Equal("also somebody's", await File.ReadAllTextAsync(notes, workspace.Token));
        Assert.Equal(JobStatus.Partial, job.Status);
        var left = Assert.IsType<ClearAllDataResult>(job.Result).NotRemoved;
        Assert.Equal(Path.Combine(workspace.DefaultStorageRoot, "lookalike", "install.git"), Assert.Single(left).Path);
    }

    [Fact(Timeout = 120_000)]
    public async Task Clearing_all_data_takes_the_history_out_of_a_game_folder_but_not_what_else_is_in_it()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var readme = Path.Combine(workspace.DefaultStorageRoot, game.Id, "readme.txt");
        BackendWorkspace.WriteFile(readme, "left here by the user");

        var started = await workspace.Settings.ClearAllDataAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId, TimeSpan.FromSeconds(120));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal("left here by the user", await File.ReadAllTextAsync(readme, workspace.Token));
        Assert.False(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git")));
        Assert.False(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id, "external.git")));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_game_folder_that_is_a_link_stops_the_move_instead_of_being_left_behind()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        var real = Path.Combine(workspace.Root, "real-place", game.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(real)!);
        Directory.Move(Path.Combine(workspace.DefaultStorageRoot, game.Id), real);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(workspace.DefaultStorageRoot, game.Id), real);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip("this account cannot create symbolic links");
        }

        var destination = Path.Combine(workspace.Root, "new-storage");
        var preview = await workspace.Settings.GetRelocationPreviewAsync(destination, workspace.Token);
        var started = await workspace.Settings.RelocateStorageAsync(destination);

        Assert.Equal("error.storageRelocation.copyFailed", preview.Error?.MessageId);
        Assert.Equal("error.storageRelocation.copyFailed", started.Error?.MessageId);
        Assert.Equal(Path.Combine(workspace.DefaultStorageRoot, game.Id), started.Error!.Args["path"]);
        Assert.False(Directory.Exists(destination));
        Assert.Equal(workspace.DefaultStorageRoot, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
    }
}
