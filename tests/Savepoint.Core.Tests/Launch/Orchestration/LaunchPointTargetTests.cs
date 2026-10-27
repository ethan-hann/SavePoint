using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Tests.Git.Engine;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// "Go back to this" puts a folder on the commit that stands for the point, and leaves a folder with no such commit where it is (D31).
public class LaunchPointTargetTests
{
    private static readonly DateTimeOffset _noon = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact(Timeout = 120_000)]
    public async Task Both_folders_of_one_capture_event_record_the_same_time_even_when_the_clock_moves_between_them()
    {
        using var rig = await LaunchRig.CreateAsync(options => options with { Time = new TickingClock(_noon) });

        var vanilla = await rig.ProfileAsync("Vanilla");

        var install = (await rig.CommitsAsync(RepoRole.Install, vanilla))[0];
        var saves = (await rig.CommitsAsync(RepoRole.External, vanilla))[0];
        Assert.NotNull(install.CapturedAt);
        Assert.Equal(install.CapturedAt, saves.CapturedAt);
    }

    [Fact(Timeout = 150_000)]
    public async Task A_folder_with_no_commit_at_or_before_the_point_is_left_as_it_is_instead_of_restored_to_a_newer_one()
    {
        var clock = new FixedClock(_noon);
        using var rig = await LaunchRig.CreateAsync(options => options with { Time = clock });
        var vanilla = await rig.ProfileAsync("Vanilla");

        // The install gets a point at 13:00; the saves get one the clock says happened at 11:00, before every install commit.
        clock.Set(_noon.AddHours(1));
        rig.WriteInstall("mods/update.txt", "a mod update");
        await rig.CaptureAsync(vanilla, "Mod update", RepoRole.Install);
        clock.Set(_noon.AddHours(-1));
        rig.WriteSave("slot2.sav", "two");
        await rig.CaptureAsync(vanilla, "Early save", RepoRole.External);
        var early = (await rig.History.GetAsync(rig.Game.Id, null, rig.Token)).Value!.Points.Single(point => point.Name == "Early save");

        // The folders move to another profile's state, so "left as it is" is visibly different from "restored to Vanilla's".
        clock.Set(_noon.AddHours(2));
        rig.WriteInstall("mods/other.txt", "another profile");
        await rig.ProfileAsync("Other");

        var preview = await rig.PreviewAsync(new LaunchTarget.Point(early.Id));

        Assert.True(preview.Install.MatchesTarget);
        Assert.Null(preview.Install.Switch);
        Assert.False(preview.Folder(RepoRole.External)!.MatchesTarget);
    }

    // Every read of the clock is a millisecond later than the one before.
    private sealed class TickingClock(DateTimeOffset start) : TimeProvider
    {
        private long _ticks;

        public override DateTimeOffset GetUtcNow() => start.AddMilliseconds(Interlocked.Increment(ref _ticks));
    }
}
