using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// Starting from an older save (D14, D15), and keeping work that belongs to no profile as a new one.
public class LaunchOlderPointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    [Fact(Timeout = 150_000)]
    public async Task Starting_from_an_older_save_attaches_the_install_and_leaves_the_saves_on_that_save()
    {
        var (rig, _, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await GitClock.NextSecondAsync(Ct);
        rig.WriteSave("slot3.sav", "third save");
        await rig.CaptureAsync(modded, "Third save");
        var older = (await rig.PreviewAsync(Tip(modded))).AvailableSaves[1];
        var olderCommit = (await rig.CommitsAsync(RepoRole.External, modded))[1];

        var job = await rig.StartAsync(new LaunchTarget.Save(modded.Id, older.Id));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var session = rig.Launch.GetSession(rig.Game.Id)!;
        Assert.True(session.StartedFromOlderPoint);
        Assert.Equal(modded.Id, session.ProfileId);
        var saves = await rig.StatusAsync(RepoRole.External);
        Assert.True(saves.IsDetached);
        Assert.Equal(olderCommit.Id, saves.HeadCommit);
        Assert.False(rig.SavesHave("slot3.sav"));
        Assert.True(rig.SavesHave("slot2.sav"));
        var install = await rig.StatusAsync(RepoRole.Install);
        Assert.False(install.IsDetached);
        Assert.Equal(modded.BranchName, install.Branch);
    }

    [Fact(Timeout = 150_000)]
    public async Task Changes_kept_as_a_new_profile_clear_the_way_for_a_folder_left_on_an_older_save()
    {
        var (rig, _, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        var oldest = (await rig.CommitsAsync(RepoRole.External, modded))[^1].Id;
        await rig.WithEngineAsync(async (engine, repo) =>
        {
            await engine.CheckoutCommitAsync(repo, oldest, null, Ct);
            return true;
        }, RepoRole.External);
        rig.WriteSave("slot9.sav", "played from an older save");

        var refused = await rig.StartAsync(Tip(modded));
        var preview = await rig.PreviewAsync(Tip(modded));

        Assert.Equal("error.launch.dirtyWorkingTree", refused.Error?.MessageId);
        Assert.Null(preview.Folder(RepoRole.External)!.DirtyOwnerProfileId);

        // "Keep as a new profile": the whole folder as it is now becomes a profile of its own.
        var kept = await rig.Workspace.CreateProfileAsync(rig.Game, "Kept");
        var launched = await rig.StartAsync(Tip(modded));

        Assert.Equal(JobStatus.Succeeded, launched.Status);
        Assert.False(rig.SavesHave("slot9.sav"));
        Assert.True(rig.SavesHave("slot2.sav"));
        var keptFiles = await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, kept.Profile.BranchName, Ct), RepoRole.External);
        Assert.Contains("slot9.sav", keptFiles);
    }
}
