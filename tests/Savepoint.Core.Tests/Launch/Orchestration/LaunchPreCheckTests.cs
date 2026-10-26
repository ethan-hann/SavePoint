using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A folder that cannot be switched is found before the first folder is touched, so install and saves never end on different profiles.
public class LaunchPreCheckTests
{
    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    [Fact(Timeout = 240_000)]
    public async Task A_saves_folder_that_is_short_of_space_stops_the_launch_before_the_install_folder_is_switched()
    {
        var (rig, vanilla, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        var installBefore = rig.SnapshotOf(RepoRole.Install);
        var savesBefore = rig.SnapshotOf(RepoRole.External);
        rig.Workspace.Disk.FreePerPath = path => path.StartsWith(rig.SavesPath, StringComparison.OrdinalIgnoreCase) ? 10 : null;

        var failed = await rig.StartAsync(Tip(vanilla));

        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Equal("error.diskSpace.low", failed.Error?.MessageId);
        Assert.Equal(installBefore, rig.SnapshotOf(RepoRole.Install));
        Assert.Equal(savesBefore, rig.SnapshotOf(RepoRole.External));
        var install = await rig.StatusAsync(RepoRole.Install);
        Assert.Equal(modded.BranchName, install.Branch);
        Assert.Null(install.InterruptedSwitch);
        Assert.Null((await rig.StatusAsync(RepoRole.External)).InterruptedSwitch);
        Assert.Empty(rig.GameProcessIds());
    }

    [Fact(Timeout = 240_000)]
    public async Task The_launch_goes_ahead_once_the_saves_folder_has_room_again()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        rig.Workspace.Disk.FreePerPath = path => path.StartsWith(rig.SavesPath, StringComparison.OrdinalIgnoreCase) ? 10 : null;
        var failed = await rig.StartAsync(Tip(vanilla));
        Assert.Equal("error.diskSpace.low", failed.Error?.MessageId);
        rig.Workspace.Disk.FreePerPath = null;

        var retried = await rig.StartAsync(Tip(vanilla));

        Assert.Equal(JobStatus.Succeeded, retried.Status);
        Assert.False(rig.InstallHas("mods/a.txt"));
        Assert.False(rig.SavesHave("slot2.sav"));
        Assert.Equal(vanilla.BranchName, (await rig.StatusAsync(RepoRole.Install)).Branch);
        Assert.Equal(vanilla.BranchName, (await rig.StatusAsync(RepoRole.External)).Branch);
        await rig.WaitForGameAsync();
    }
}
