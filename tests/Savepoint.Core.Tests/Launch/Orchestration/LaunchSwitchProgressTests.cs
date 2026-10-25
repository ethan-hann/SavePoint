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

    [Fact(Timeout = 180_000)]
    public async Task A_switch_tells_the_screen_when_it_is_reading_the_files_back_and_stops_telling_it_when_it_is_done()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;

        await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));

        var reports = rig.Events.Select(session => session.State).OfType<SessionState.Switching>().Select(state => state.Progress).ToList();
        Assert.Contains(reports, report => report.Finishing);
        Assert.False(reports[^1].Finishing);

        // The check keeps the counts the writing ended with.
        var wrapUp = reports.First(report => report.Finishing);
        Assert.True(wrapUp.FilesDone > 0);
        Assert.Equal(wrapUp.BytesTotal, wrapUp.BytesDone);
    }

    [Fact(Timeout = 180_000)]
    public async Task A_switch_that_writes_in_both_folders_adds_the_second_folders_bytes_and_files_to_the_first_ones()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");
        rig.WriteInstall("data/base.pak", "a patched install file of 31 bytes");
        rig.WriteSave("slot1.sav", "a longer save than the first one was");
        var modded = await rig.ProfileAsync("Modded");
        Assert.Equal(modded.Id, (await rig.PreviewAsync(new LaunchTarget.ProfileTip(modded.Id))).ProfileId);

        // Going back to Vanilla writes the install file (12 bytes, "base content") and then the save (8 bytes, "save one").
        await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));

        var reports = rig.Events.Select(session => session.State).OfType<SessionState.Switching>().Select(state => state.Progress).ToList();
        var last = reports[^1];
        Assert.Equal(20, last.BytesTotal);
        Assert.Equal(20, last.BytesDone);
        Assert.Equal(2, last.FilesDone);
        Assert.Equal(2, last.FilesTotal);

        // The first folder ends at its own 12 bytes, and the second starts counting from there.
        Assert.Contains(reports, report => report.BytesDone == 12 && report.FilesDone == 1);
        Assert.All(reports.Where(report => report.BytesDone is not null), report => Assert.True(report.BytesDone <= report.BytesTotal));
        var done = reports.Select(report => report.BytesDone ?? 0).ToList();
        Assert.Equal(done.Order(), done);
    }
}
