using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// "Go back to this" leaves a folder as it is when the profile has no commit for the point there. What the player may do after play
// is decided from where that folder stood when the game started (D37).
public class LaunchLeftFolderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IReadOnlyList<string> Subjects(IReadOnlyList<CommitInfo> commits) => [.. commits.Select(commit => commit.Subject)];

    // A profile whose first point holds only the install, then a mod update, then (when asked) two saves of a folder added later.
    // The first point is older than the profile's tip and than every save, so going back to it leaves the saves folder as it is.
    private static async Task<(LaunchRig Rig, Profile Profile, HistoryPointId InstallOnlyPoint)> WithInstallOnlyPointAsync(bool savesSaved)
    {
        var rig = await LaunchRig.CreateAsync(withSaves: false, gameArguments: ["--exit-after", "800"]);
        try
        {
            var profile = await rig.ProfileAsync("Vanilla");
            rig.WriteInstall("mods/update.txt", "a mod update");
            await rig.CaptureAsync(profile, "Mod update", RepoRole.Install);
            await rig.AddSavesFolderAsync();
            if (savesSaved)
            {
                await rig.CaptureAsync(profile, "First saves", RepoRole.External);
                rig.WriteSave("slot2.sav", "second save");
                await rig.CaptureAsync(profile, "Second saves", RepoRole.External);
            }

            var first = (await rig.History.GetAsync(rig.Game.Id, null, rig.Token)).Value!.Points.Single(point => point.Name == "Vanilla");
            return (rig, profile, first.Id);
        }
        catch
        {
            rig.Dispose();
            throw;
        }
    }

    private static async Task<SessionState.Exited> PlayAsync(LaunchRig rig, LaunchTarget target)
    {
        await rig.StartAsync(target);
        return Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);
    }

    private static async Task<SessionState.Completed> CompleteAsync(LaunchRig rig, SessionCompletion completion)
    {
        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, completion)).Ok);
        return Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);
    }

    [Fact(Timeout = 150_000)]
    public async Task Continuing_from_here_saves_both_folders_when_the_folder_that_was_left_started_on_the_profile_s_own_history()
    {
        var (rig, vanilla, first) = await WithInstallOnlyPointAsync(savesSaved: true);
        using var _ = rig;

        var exited = await PlayAsync(rig, new LaunchTarget.Point(first));
        rig.WriteSave("played.sav", "progress");
        var done = await CompleteAsync(rig, new SessionCompletion.MoveForward("Continued"));

        Assert.Contains(CompletionChoice.MoveForward, exited.Options.Choices);
        Assert.Null(done.Error);
        Assert.All(Assert.IsType<CaptureResult>(done.Result).Folders, folder => Assert.True(folder.Ok, folder.Error?.Detail));
        Assert.Equal(["Continued", "Second saves", "First saves"], Subjects(await rig.CommitsAsync(RepoRole.External, vanilla)));
        Assert.Equal(["Vanilla"], Subjects(await rig.CommitsAsync(RepoRole.Install, vanilla)));
    }

    [Fact(Timeout = 150_000)]
    public async Task Continuing_from_here_is_not_offered_when_the_folder_that_was_left_has_no_history_of_this_profile()
    {
        var (rig, _, first) = await WithInstallOnlyPointAsync(savesSaved: false);
        using var _ = rig;

        var exited = await PlayAsync(rig, new LaunchTarget.Point(first));
        var refused = await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.MoveForward("Continued"));

        Assert.Equal([CompletionChoice.ForkNewProfile, CompletionChoice.Skip], exited.Options.Choices);
        Assert.Equal(CompletionChoice.ForkNewProfile, exited.Options.Default);
        Assert.Equal("error.launch.completionNotOffered", refused.Error?.MessageId);
    }

    [Fact(Timeout = 150_000)]
    public async Task Keeping_the_session_as_a_new_profile_refuses_a_left_folder_that_moved_after_the_game_started()
    {
        var (rig, vanilla, first) = await WithInstallOnlyPointAsync(savesSaved: true);
        using var _ = rig;
        var savesBefore = Subjects(await rig.CommitsAsync(RepoRole.External, vanilla));

        await PlayAsync(rig, new LaunchTarget.Point(first));

        // The saves folder is put back on the first of its two points, which does not come after the point it was on when the game started.
        var firstSaves = (await rig.CommitsAsync(RepoRole.External, vanilla))[^1].Id;
        await rig.WithEngineAsync(
            async (engine, repo) =>
            {
                await engine.ForceCheckoutCommitAsync(repo, firstSaves, Ct);
                return true;
            },
            RepoRole.External);
        var done = await CompleteAsync(rig, new SessionCompletion.ForkNewProfile("Kept", "Kept point"));

        var result = Assert.IsType<CaptureResult>(done.Result);
        Assert.True(result.Folders.Single(folder => folder.Role == RepoRole.Install).Ok);
        Assert.Equal("error.launch.sessionMoved", result.Folders.Single(folder => folder.Role == RepoRole.External).Error?.MessageId);
        Assert.Equal(savesBefore, Subjects(await rig.CommitsAsync(RepoRole.External, vanilla)));
    }

    // The first profile sees the point; the second was made from the same state, so the saves folder sits on the second one's branch.
    [Theory(Timeout = 150_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_left_folder_on_another_profile_s_branch_is_skipped_by_the_save_and_never_fails_it(bool update)
    {
        using var rig = await LaunchRig.CreateAsync(withSaves: false, gameArguments: ["--exit-after", "800"]);
        var vanilla = await rig.ProfileAsync("Vanilla");
        rig.WriteInstall("mods/update.txt", "a mod update");
        await rig.CaptureAsync(vanilla, "Mod update", RepoRole.Install);
        await rig.AddSavesFolderAsync();
        await rig.CaptureAsync(vanilla, "First saves", RepoRole.External);
        var other = await rig.ProfileAsync("Other");
        var modUpdate = (await rig.History.GetAsync(rig.Game.Id, null, rig.Token)).Value!.Points.Single(point => point.Name == "Mod update");

        var exited = await PlayAsync(rig, new LaunchTarget.Point(modUpdate.Id));
        rig.WriteInstall("settings.ini", "played");
        rig.WriteSave("played.sav", "progress");
        var done = await CompleteAsync(rig, update ? new SessionCompletion.UpdateExisting("Updated") : new SessionCompletion.CaptureNew("Played"));

        Assert.DoesNotContain(CompletionChoice.ForkNewProfile, exited.Options.Choices);
        Assert.Null(done.Error);
        var result = Assert.IsType<CaptureResult>(done.Result);
        Assert.All(result.Folders, folder => Assert.True(folder.Ok, folder.Error?.Detail));
        Assert.NotNull(result.PointId);
        Assert.Equal(3, (await rig.CommitsAsync(RepoRole.Install, vanilla)).Count);
        Assert.Equal(["First saves"], Subjects(await rig.CommitsAsync(RepoRole.External, vanilla)));
        Assert.Equal(other.BranchName, (await rig.StatusAsync(RepoRole.External)).Branch);
        Assert.True(rig.SavesHave("played.sav"));
    }
}
