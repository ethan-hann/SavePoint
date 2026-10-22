using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A file the game's ignore list names is never in the way of a launch, whether or not an earlier point recorded it: the switch leaves
// it exactly as it is on disk, and the dialog never asks the player to resolve it.
public class LaunchIgnoredFileTests
{
    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    [Fact(Timeout = 150_000)]
    public async Task A_launch_switches_over_a_file_a_pattern_added_later_names_and_leaves_that_file_alone()
    {
        using var rig = await LaunchRig.CreateAsync();
        rig.WriteInstall("logs/run.log", "recorded under A");
        var a = await rig.ProfileAsync("A");
        File.Delete(Path.Combine(rig.InstallPath, "logs", "run.log"));
        rig.WriteInstall("data/b-only.txt", "only B has this");
        await rig.ProfileAsync("B");

        // The player notices the game rewrites logs/ on every run and ignores it; B is loaded and the game has written its own copy.
        var edited = await rig.Workspace.Games.UpdateAsync(rig.Game with { IgnorePaths = ["logs/"] });
        Assert.True(edited.Ok, edited.Error?.Detail);
        rig.WriteInstall("logs/run.log", "written by the game while B was loaded");

        var preview = await rig.PreviewAsync(Tip(a));
        var job = await rig.StartAsync(Tip(a));

        Assert.False(preview.Install.Dirty);
        Assert.Null(preview.Install.HeldBy);
        Assert.True(job.Status == JobStatus.Succeeded, $"{job.Status}: {job.Error?.MessageId} {job.Error?.Detail}");
        Assert.Equal("written by the game while B was loaded", rig.ReadInstall("logs/run.log"));
        Assert.False(rig.InstallHas("data/b-only.txt"));

        // Read the way the player's next launch reads it: on A, with nothing unsaved.
        var after = await rig.PreviewAsync(Tip(a));
        Assert.True(after.Install.MatchesTarget);
        Assert.False(after.Install.Dirty);
    }
}
