using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// Playing on from an older save and then choosing between a new profile and moving the profile forward (D14).
public class LaunchContinueTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    private static IReadOnlyList<string> Subjects(IReadOnlyList<CommitInfo> commits) => [.. commits.Select(commit => commit.Subject)];

    [Fact(Timeout = 280_000)]
    public async Task An_older_save_can_be_kept_as_a_new_profile_or_continued_and_a_folder_that_moved_is_refused()
    {
        using var rig = await LaunchRig.CreateAsync(gameArguments: ["--save-every", "150", "--exit-after", "1200"]);
        var vanilla = await rig.ProfileAsync("Vanilla");
        await Task.Delay(1100, Ct);
        rig.WriteSave("slot2.sav", "two");
        await rig.CaptureAsync(vanilla, "Second");
        await Task.Delay(1100, Ct);
        rig.WriteSave("slot3.sav", "three");
        await rig.CaptureAsync(vanilla, "Third");
        var second = (await rig.PreviewAsync(Tip(vanilla))).AvailableSaves[1];

        // Played from the second save of three: the choices are the older-point ones, and one point is newer than where it began.
        await rig.StartAsync(new LaunchTarget.Save(vanilla.Id, second.Id));
        var exited = Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);
        Assert.Equal([CompletionChoice.ForkNewProfile, CompletionChoice.MoveForward, CompletionChoice.Skip], exited.Options.Choices);
        Assert.Equal(CompletionChoice.ForkNewProfile, exited.Options.Default);
        Assert.Null(exited.Options.UpdateTarget);
        Assert.Equal(1, exited.Options.NewerPointCount);

        // Kept as a new profile: it starts at the second save with what was played on top, and the original is untouched.
        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.ForkNewProfile("Branched", "Branched point"))).Ok);
        var forked = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);
        var branched = (await rig.Profiles.ListAsync(rig.Game.Id, Ct)).Value!.Single(profile => profile.Name == "Branched");
        Assert.Null(forked.Error);
        Assert.Equal(branched.Id, Assert.IsType<CaptureResult>(forked.Result).ProfileId);
        Assert.Equal(["Branched point", "Second", "Vanilla"], Subjects(await rig.CommitsAsync(RepoRole.External, branched)));
        Assert.Equal(["Third", "Second", "Vanilla"], Subjects(await rig.CommitsAsync(RepoRole.External, vanilla)));
        Assert.Equal(branched.BranchName, (await rig.StatusAsync(RepoRole.External)).Branch);

        // Continued instead: the profile moves on from the second save and the third leaves it.
        await rig.StartAsync(new LaunchTarget.Save(vanilla.Id, second.Id));
        await rig.WaitForAsync(session => session.State is SessionState.Exited, "Exited");
        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.MoveForward("Moved on"))).Ok);
        var moved = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);
        Assert.Null(moved.Error);
        Assert.All(Assert.IsType<CaptureResult>(moved.Result).Folders, folder => Assert.True(folder.Ok));
        Assert.Equal(["Moved on", "Second", "Vanilla"], Subjects(await rig.CommitsAsync(RepoRole.External, vanilla)));
        var saves = await rig.StatusAsync(RepoRole.External);
        Assert.Equal(vanilla.BranchName, saves.Branch);
        Assert.False(saves.IsDetached);

        // A folder switched to somewhere the session did not begin cannot take the new progress: that folder is refused, the other is not.
        var again = (await rig.PreviewAsync(Tip(vanilla))).AvailableSaves[1];
        var firstCommit = (await rig.CommitsAsync(RepoRole.External, vanilla))[^1].Id;
        await rig.StartAsync(new LaunchTarget.Save(vanilla.Id, again.Id));
        await rig.WaitForAsync(session => session.State is SessionState.Exited, "Exited");
        await rig.WithEngineAsync(async (engine, repo) =>
        {
            await engine.ForceCheckoutCommitAsync(repo, firstCommit, Ct);
            return true;
        }, RepoRole.External);
        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.MoveForward("Too late"))).Ok);
        var refused = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);

        var result = Assert.IsType<CaptureResult>(refused.Result);
        Assert.Null(refused.Error);
        Assert.True(result.Folders.Single(folder => folder.Role == RepoRole.Install).Ok);
        Assert.Equal("error.launch.sessionMoved", result.Folders.Single(folder => folder.Role == RepoRole.External).Error?.MessageId);
        Assert.Equal(["Moved on", "Second", "Vanilla"], Subjects(await rig.CommitsAsync(RepoRole.External, vanilla)));
    }
}
