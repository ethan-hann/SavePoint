using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Services;
using Savepoint.Core.Tests.Backend;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A saves folder added after a profile existed: the profile never saved it, so a launch leaves it alone and the next save adds it (D31).
public class LaunchNewSavesFolderTests
{
    private static async Task<(LaunchRig Rig, Profile Vanilla, string Saves)> WithSavesAddedLaterAsync(IReadOnlyList<string>? gameArguments = null)
    {
        var rig = await LaunchRig.CreateAsync(withSaves: false, gameArguments: gameArguments);
        try
        {
            var vanilla = await rig.ProfileAsync("Vanilla");
            var saves = Path.Combine(rig.Workspace.GamesRoot, "later-saves");
            BackendWorkspace.WriteFile(Path.Combine(saves, "slot1.sav"), "the player's own save");
            var updated = await rig.Workspace.Games.UpdateAsync(rig.Game with { External = new ExternalLocation(saves) });
            Assert.True(updated.Ok, updated.Error?.Detail);
            return (rig, vanilla, saves);
        }
        catch
        {
            rig.Dispose();
            throw;
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task The_preview_of_a_profile_that_never_saved_the_new_folder_succeeds_and_does_not_touch_that_folder()
    {
        var (rig, vanilla, saves) = await WithSavesAddedLaterAsync();
        using var _ = rig;

        var preview = await rig.Launch.GetPreviewAsync(rig.Game.Id, new LaunchTarget.ProfileTip(vanilla.Id), rig.Token);

        Assert.True(preview.Ok, preview.Error?.Detail);
        Assert.Null(preview.Value.External);
        Assert.Equal("the player's own save", await File.ReadAllTextAsync(Path.Combine(saves, "slot1.sav"), rig.Token));
    }

    [Fact(Timeout = 150_000)]
    public async Task A_launch_leaves_the_new_folder_as_it_is_and_the_save_after_play_adds_it_to_the_profile()
    {
        var (rig, vanilla, saves) = await WithSavesAddedLaterAsync(["--exit-after", "800"]);
        using var _ = rig;

        var job = await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));
        await rig.WaitForAsync<SessionState.Exited>();
        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("After play"))).Ok);
        var done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal("the player's own save", await File.ReadAllTextAsync(Path.Combine(saves, "slot1.sav"), rig.Token));
        Assert.Null(done.Error);
        Assert.All(Assert.IsType<CaptureResult>(done.Result).Folders, folder => Assert.True(folder.Ok, folder.Error?.Detail));
        var game = (await rig.Workspace.Games.ListAsync(rig.Token)).Value!.Single();
        var savedFiles = await rig.Workspace.Backend.Services.Storage.ReadAsync(
            storage => storage.Engine.ListTreeFilesAsync(GameFolders.Repos(rig.Workspace.Backend.Services, storage, game).Single(repo => repo.Role == RepoRole.External), vanilla.BranchName, rig.Token),
            rig.Token);
        Assert.Contains("slot1.sav", savedFiles);
    }
}
