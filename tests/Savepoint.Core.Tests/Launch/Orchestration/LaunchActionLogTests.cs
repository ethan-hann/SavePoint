using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// "SavePoint deleted my save" has to be answerable from the log (D34, D41): what a discard, a switch and a save that skipped a folder did.
public class LaunchActionLogTests
{
    private readonly RecordingDiagnosticLog _log = new();

    private Task<LaunchRig> RigAsync(bool withSaves = true, IReadOnlyList<string>? gameArguments = null) =>
        LaunchRig.CreateAsync(options => options with { Log = _log }, gameArguments, withSaves: withSaves);

    [Fact(Timeout = 120_000)]
    public async Task A_discard_logs_the_folder_the_point_it_went_back_to_and_how_many_files_it_restored_and_removed()
    {
        using var rig = await RigAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");
        var point = (await rig.CommitsAsync(RepoRole.Install, vanilla)).Single().Id;
        rig.WriteInstall("data/base.pak", "edited by the player");
        rig.WriteInstall("stray.txt", "new");
        rig.WriteInstall("stray2.txt", "new too");

        var discarded = await rig.Launch.DiscardChangesAsync(rig.Game.Id, RepoRole.Install);

        Assert.True(discarded.Ok, discarded.Error?.Detail);
        var line = Assert.Single(_log.Containing("Discard", rig.Game.Id, "install"));
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains(point, line.Message, StringComparison.Ordinal);
        Assert.Contains("1 changed or deleted files restored", line.Message, StringComparison.Ordinal);
        Assert.Contains("2 new files removed", line.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_switch_logs_each_folder_with_the_point_it_left_and_the_point_it_went_to()
    {
        using var rig = await RigAsync();
        var (vanilla, modded) = await rig.BuildTwoProfilesAsync();
        var vanillaPoint = (await rig.CommitsAsync(RepoRole.Install, vanilla)).Single().Id;
        var moddedPoint = (await rig.CommitsAsync(RepoRole.Install, modded)).First().Id;

        await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));

        var line = Assert.Single(_log.Containing("Switch", rig.Game.Id, "install"));
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains(moddedPoint, line.Message, StringComparison.Ordinal);
        Assert.Contains(vanillaPoint, line.Message, StringComparison.Ordinal);
        Assert.True(line.Message.IndexOf(moddedPoint, StringComparison.Ordinal) < line.Message.IndexOf(vanillaPoint, StringComparison.Ordinal), "the point left comes first");
        Assert.Single(_log.Containing("Switch", rig.Game.Id, "saves"));
    }

    [Fact(Timeout = 150_000)]
    public async Task A_save_that_skipped_a_folder_logs_which_folder_and_why()
    {
        using var rig = await RigAsync(withSaves: false);
        var vanilla = await rig.ProfileAsync("Vanilla");
        rig.WriteInstall("mods/update.txt", "a mod update");
        await rig.CaptureAsync(vanilla, "Mod update", RepoRole.Install);
        await rig.AddSavesFolderAsync();
        await rig.CaptureAsync(vanilla, "First saves", RepoRole.External);
        var other = await rig.ProfileAsync("Other");
        var modUpdate = (await rig.History.GetAsync(rig.Game.Id, null, rig.Token)).Value!.Points.Single(point => point.Name == "Mod update");
        await rig.StartAsync(new LaunchTarget.Point(modUpdate.Id));
        await rig.WaitForGameAsync();
        rig.WriteSave("played.sav", "progress");
        Assert.True((await rig.Launch.MarkFinishedAsync(rig.Game.Id)).Ok);
        await rig.WaitForAsync<SessionState.Exited>();

        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("Played"))).Ok);
        await rig.WaitForAsync<SessionState.Completed>();

        var line = Assert.Single(_log.Containing("Save", rig.Game.Id, "saves folder", "left alone"));
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains(other.BranchName, line.Message, StringComparison.Ordinal);
    }
}
