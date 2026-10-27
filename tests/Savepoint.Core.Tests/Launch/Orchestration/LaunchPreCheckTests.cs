using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

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

    private const long _floor = 1_000;

    // What going from Vanilla to Modded adds in each folder (the stand-in files' sizes): the install folder's is the larger.
    private const long _installGrowth = 28;
    private const long _savesGrowth = 11;

    private static Func<BackendOptions, BackendOptions> SmallFloor => options => options with { Engine = new GitEngineOptions { MinFreeBytesForCheckout = _floor } };

    // The drive has room for either folder's growth but not for both together.
    private static async Task<(LaunchRig Rig, Profile Vanilla, Profile Modded)> OnVanillaWithRoomForOneFolderAsync(long free)
    {
        var (rig, vanilla, modded) = await LaunchRig.WithTwoProfilesAsync(SmallFloor);
        try
        {
            var down = await rig.SwitchAsync(Tip(vanilla));
            Assert.Equal(JobStatus.Succeeded, down.Status);
            rig.Workspace.Disk.FreePerPath = path => path.StartsWith(rig.Workspace.GamesRoot, StringComparison.OrdinalIgnoreCase) ? free : null;
            return (rig, vanilla, modded);
        }
        catch
        {
            rig.Dispose();
            throw;
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task Folders_on_one_drive_that_each_fit_but_not_together_stop_the_launch_before_any_is_switched()
    {
        var (rig, _, modded) = await OnVanillaWithRoomForOneFolderAsync(_floor + _installGrowth);
        using var _ = rig;
        var installBefore = rig.SnapshotOf(RepoRole.Install);
        var savesBefore = rig.SnapshotOf(RepoRole.External);

        var failed = await rig.StartAsync(Tip(modded));

        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Equal("error.diskSpace.low", failed.Error?.MessageId);
        Assert.Equal(_floor + _installGrowth + _savesGrowth, failed.Error!.Args["requiredBytes"]);
        Assert.Equal(_floor + _installGrowth, failed.Error.Args["freeBytes"]);
        Assert.Equal(installBefore, rig.SnapshotOf(RepoRole.Install));
        Assert.Equal(savesBefore, rig.SnapshotOf(RepoRole.External));
        Assert.Null((await rig.StatusAsync(RepoRole.Install)).InterruptedSwitch);
        Assert.Null((await rig.StatusAsync(RepoRole.External)).InterruptedSwitch);
    }

    [Fact(Timeout = 240_000)]
    public async Task Folders_on_different_drives_are_each_judged_on_their_own()
    {
        var (rig, _, modded) = await OnVanillaWithRoomForOneFolderAsync(_floor + _installGrowth);
        using var _ = rig;
        rig.Workspace.Disk.VolumePerPath = path => path.StartsWith(rig.SavesPath, StringComparison.OrdinalIgnoreCase) ? "saves-drive" : "install-drive";

        var switched = await rig.SwitchAsync(Tip(modded));

        Assert.Equal(JobStatus.Succeeded, switched.Status);
        Assert.True(rig.InstallHas("mods/a.txt"));
        Assert.True(rig.SavesHave("slot2.sav"));
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
