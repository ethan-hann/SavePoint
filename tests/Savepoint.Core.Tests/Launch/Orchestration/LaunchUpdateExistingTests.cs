using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// "Update existing" replaces the one point the player named and never rewrites another (D31).
public class LaunchUpdateExistingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    [Fact(Timeout = 200_000)]
    public async Task An_update_leaves_an_install_only_point_alone_when_the_install_changed_during_play()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");

        // The install's newest point (a mod update) belongs to another capture than the saves' newest point.
        await Task.Delay(1100, Ct);
        rig.WriteInstall("mods/update.txt", "a mod update");
        await rig.CaptureAsync(vanilla, "Mod update", RepoRole.Install);
        await Task.Delay(1100, Ct);
        rig.WriteSave("slot2.sav", "second save");
        await rig.CaptureAsync(vanilla, "Second save", RepoRole.External);
        var modUpdate = (await rig.CommitsAsync(RepoRole.Install, vanilla))[0];

        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForGameAsync();
        rig.WriteInstall("settings.ini", "written by the game");
        rig.WriteSave("slot2.sav", "played on");
        rig.KillGame();
        await rig.WaitForAsync<SessionState.Exited>();

        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.UpdateExisting("Updated"))).Ok);
        var done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);

        Assert.Null(done.Error);
        var result = Assert.IsType<CaptureResult>(done.Result);
        Assert.All(result.Folders, folder => Assert.True(folder.Ok, folder.Error?.Detail));

        // The mod update point is still there, in the install's history and in the history list.
        var install = await rig.CommitsAsync(RepoRole.Install, vanilla);
        Assert.Contains(modUpdate.Id, install.Select(commit => commit.Id));
        Assert.Equal(["Updated", "Mod update", "Vanilla"], install.Select(commit => commit.Subject));
        var history = (await rig.History.GetAsync(rig.Game.Id, null, rig.Token)).Value!;
        var kept = Assert.Single(history.Points, point => point.Name == "Mod update");
        Assert.Equal(HistoryPointKind.VersionAndMods, kept.Kind);

        // The update stays one point: the saves point was replaced in place and the install change joined it.
        var saves = await rig.CommitsAsync(RepoRole.External, vanilla);
        Assert.Equal(["Updated", "Vanilla"], saves.Select(commit => commit.Subject));
        Assert.Equal(result.PointId?.Value, install[0].CaptureId);
        Assert.Equal(result.PointId?.Value, saves[0].CaptureId);
        Assert.Contains("settings.ini", await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, vanilla.BranchName, rig.Token), RepoRole.Install));
    }
}
