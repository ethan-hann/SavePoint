using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A saves folder that one profile has saved and another has not (it was added after both existed): the folder is read like any other
// before the launch, and the profile that never saved it starts a history of its own there (D37).
public class LaunchSavesOfOtherProfilesTests
{
    // Two profiles made before the game had a saves folder; the folder is then added and only the first profile saves it.
    private static async Task<(LaunchRig Rig, Profile Saved, Profile Unsaved)> WithOneSavedAsync(IReadOnlyList<string>? gameArguments = null)
    {
        var rig = await LaunchRig.CreateAsync(withSaves: false, gameArguments: gameArguments);
        try
        {
            var saved = await rig.ProfileAsync("Vanilla");
            rig.WriteInstall("mods/a.txt", "mod a");
            var unsaved = await rig.ProfileAsync("Modded");
            await rig.AddSavesFolderAsync();
            await rig.CaptureAsync(saved, "Saves of Vanilla", RepoRole.External);
            rig.WriteSave("slot2.sav", "second save");
            await rig.CaptureAsync(saved, "More saves of Vanilla", RepoRole.External);
            return (rig, saved, unsaved);
        }
        catch
        {
            rig.Dispose();
            throw;
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task The_other_profile_s_unsaved_changes_in_a_saves_folder_this_profile_never_saved_block_the_launch_and_name_their_owner()
    {
        var (rig, saved, unsaved) = await WithOneSavedAsync();
        using var _ = rig;
        rig.WriteSave("progress.sav", "played, not saved");

        var preview = await rig.PreviewAsync(new LaunchTarget.ProfileTip(unsaved.Id));

        var saves = Assert.IsType<FolderLaunchState>(preview.External);
        Assert.True(saves.Dirty);
        Assert.Equal(saved.Id, saves.DirtyOwnerProfileId);
        var started = await rig.Launch.StartAsync(rig.Game.Id, new LaunchTarget.ProfileTip(unsaved.Id));
        Assert.True(started.Ok, started.Error?.Detail);
        var job = await rig.WaitForJobAsync(started.Value.JobId);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("error.launch.dirtyWorkingTree", job.Error?.MessageId);
        Assert.Equal("played, not saved", rig.ReadSave("progress.sav"));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_saves_folder_with_nothing_unsaved_is_read_as_matching_and_left_as_it_is()
    {
        var (rig, _, unsaved) = await WithOneSavedAsync();
        using var _ = rig;

        var preview = await rig.PreviewAsync(new LaunchTarget.ProfileTip(unsaved.Id));

        var saves = Assert.IsType<FolderLaunchState>(preview.External);
        Assert.True(saves.MatchesTarget);
        Assert.False(saves.Dirty);
        Assert.Equal("the player's own save", rig.ReadSave("slot1.sav"));
    }

    [Fact(Timeout = 150_000)]
    public async Task The_first_save_after_play_of_a_profile_that_never_saved_the_folder_starts_a_history_of_its_own()
    {
        var (rig, saved, unsaved) = await WithOneSavedAsync(["--exit-after", "800"]);
        using var _ = rig;
        var savedBefore = (await rig.CommitsAsync(RepoRole.External, saved)).Select(commit => commit.Id).ToList();

        await rig.StartAsync(new LaunchTarget.ProfileTip(unsaved.Id));
        await rig.WaitForAsync<SessionState.Exited>();
        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("After play"))).Ok);
        var done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);

        Assert.Null(done.Error);
        Assert.All(Assert.IsType<CaptureResult>(done.Result).Folders, folder => Assert.True(folder.Ok, folder.Error?.Detail));
        var own = Assert.Single(await rig.CommitsAsync(RepoRole.External, unsaved));
        Assert.DoesNotContain(own.Id, savedBefore);
        Assert.Equal(savedBefore, (await rig.CommitsAsync(RepoRole.External, saved)).Select(commit => commit.Id));
        Assert.Equal("the player's own save", rig.ReadSave("slot1.sav"));
    }
}
