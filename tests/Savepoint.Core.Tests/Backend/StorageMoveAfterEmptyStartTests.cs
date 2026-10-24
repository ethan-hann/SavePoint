using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Backend;

// "Start with an empty folder" leaves SavePoint's own folder with no history in it; moving that folder must still work (D37).
public class StorageMoveAfterEmptyStartTests
{
    private static async Task<(BackendWorkspace Workspace, GameDefinition Game)> EmptyStartAsync(bool withProfile, bool confirmed = true)
    {
        var workspace = new BackendWorkspace();
        try
        {
            await workspace.AppStatus.CheckAsync(workspace.Token);
            var game = await workspace.AddGameAsync();
            if (withProfile)
            {
                await workspace.CreateProfileAsync(game);
            }

            await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
            BackendWorkspace.MoveFolder(workspace.DefaultStorageRoot, workspace.DefaultStorageRoot + "-away");
            if (!confirmed)
            {
                File.Delete(Path.Combine(game.Install.Path, ConsentMarker.FileName));
                File.Delete(Path.Combine(game.External!.Path, ConsentMarker.FileName));
            }

            workspace.Restart();
            var started = await workspace.AppStatus.StartWithEmptyStorageAsync(workspace.Token);
            Assert.True(started.Ok, started.Error?.Detail);
            return (workspace, game);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    // A listed game whose folder cannot be confirmed (its drive is not connected) gets no repository from the empty start, so the new folder holds nothing at all.
    private static async Task<BackendWorkspace> EmptyStartWithUnconfirmedGameAsync() =>
        (await EmptyStartAsync(withProfile: true, confirmed: false)).Workspace;

    [Fact(Timeout = 120_000)]
    public async Task A_move_is_allowed_from_an_empty_folder_that_holds_no_repository_for_the_listed_games()
    {
        using var workspace = await EmptyStartWithUnconfirmedGameAsync();
        var destination = Path.Combine(workspace.Root, "moved-storage");

        var preview = (await workspace.Settings.GetRelocationPreviewAsync(destination, workspace.Token)).Value!;
        var started = await workspace.Settings.RelocateStorageAsync(destination);

        Assert.Null(preview.BlockedBy?.MessageId);
        Assert.True(started.Ok, started.Error?.MessageId);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId, TimeSpan.FromSeconds(120));
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(destination, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
    }

    // The guard still stops a folder that holds something that is not SavePoint's: it may be a drive that is not the one with the history.
    [Fact(Timeout = 120_000)]
    public async Task A_move_is_still_refused_from_a_folder_with_other_files_and_none_of_the_listed_games()
    {
        using var workspace = await EmptyStartWithUnconfirmedGameAsync();
        BackendWorkspace.WriteFile(Path.Combine(workspace.DefaultStorageRoot, "someone-elses-file.txt"), "not ours");
        var destination = Path.Combine(workspace.Root, "moved-storage");

        var preview = (await workspace.Settings.GetRelocationPreviewAsync(destination, workspace.Token)).Value!;
        var started = await workspace.Settings.RelocateStorageAsync(destination);

        Assert.Equal("error.storage.unreachable", preview.BlockedBy?.MessageId);
        Assert.Equal("error.storage.unreachable", started.Error?.MessageId);
        Assert.False(Directory.Exists(destination));
    }

    [Theory(Timeout = 120_000)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_move_is_allowed_from_the_empty_folder_before_any_game_has_saved(bool withProfile)
    {
        var (workspace, _) = await EmptyStartAsync(withProfile);
        using var _ = workspace;
        var destination = Path.Combine(workspace.Root, "moved-storage");

        var preview = (await workspace.Settings.GetRelocationPreviewAsync(destination, workspace.Token)).Value!;
        var started = await workspace.Settings.RelocateStorageAsync(destination);

        Assert.Null(preview.BlockedBy?.MessageId);
        Assert.True(started.Ok, started.Error?.MessageId);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId, TimeSpan.FromSeconds(120));
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(destination, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
    }
}
