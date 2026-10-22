using Savepoint.Core.Api;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// The progress of a switch that puts back two folders: bytes and files add up across them.
public class LaunchSwitchProgressTests
{
    [Fact(Timeout = 180_000)]
    public async Task A_switch_of_both_folders_reports_the_bytes_it_writes_once_and_never_goes_backwards()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;

        await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));

        // Going back to Vanilla rewrites one install file (12 bytes) and removes the mods and the second save.
        var reports = rig.Events.Select(session => session.State).OfType<SessionState.Switching>().Select(state => state.Progress).ToList();
        Assert.NotEmpty(reports);
        var last = reports[^1];
        Assert.Equal(12, last.BytesTotal);
        Assert.Equal(12, last.BytesDone);
        Assert.Equal(4, last.FilesDone);
        Assert.Equal(4, last.FilesTotal);
        Assert.All(reports.Where(report => report.BytesDone is not null), report => Assert.True(report.BytesDone <= report.BytesTotal));
        var done = reports.Select(report => report.BytesDone ?? 0).ToList();
        Assert.Equal(done.Order(), done);
    }
}
