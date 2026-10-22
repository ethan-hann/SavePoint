using System.Diagnostics;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A launch that waits for the game's lock decides what it is doing once it has the lock, not before (D31).
public class LaunchQueuedStartTests
{
    private static readonly TimeSpan _held = TimeSpan.FromSeconds(20);

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    // Holds the next capture at its first free-space question, with the game's lock, so a launch started meanwhile has to queue.
    private sealed class HeldCapture : IDisposable
    {
        private readonly ManualResetEventSlim _reached = new();
        private readonly ManualResetEventSlim _release = new();

        public HeldCapture(LaunchRig rig)
        {
            rig.Workspace.Disk.FreePerPath = path =>
            {
                if (path.StartsWith(rig.Workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase)
                    && new StackTrace().ToString().Contains("CaptureCoreAsync", StringComparison.Ordinal))
                {
                    _reached.Set();
                    _release.Wait(_held);
                }

                return null;
            };
        }

        public bool Reached(CancellationToken token) => _reached.Wait(_held, token);

        public void Release() => _release.Set();

        public void Dispose()
        {
            _release.Set();
            _reached.Dispose();
            _release.Dispose();
        }
    }

    [Fact(Timeout = 150_000)]
    public async Task A_save_that_stops_being_the_newest_while_the_launch_waits_is_started_as_an_older_point()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");
        var newest = (await rig.PreviewAsync(Tip(vanilla))).AvailableSaves[0];
        using var held = new HeldCapture(rig);
        rig.WriteSave("slot2.sav", "two");
        var capture = await rig.Profiles.CaptureAsync(rig.Game.Id, vanilla.Id, "Second");
        Assert.True(capture.Ok, capture.Error?.Detail);
        Assert.True(held.Reached(rig.Token), "the capture never reached the engine");

        // Right now the save is the newest, so the launch is judged a plain one; the capture then adds a newer save before the switch.
        var started = await rig.Launch.StartAsync(rig.Game.Id, new LaunchTarget.Save(vanilla.Id, newest.Id));
        Assert.True(started.Ok, started.Error?.Detail);
        held.Release();
        await rig.WaitForJobAsync(capture.Value.JobId);
        var launch = await rig.WaitForJobAsync(started.Value.JobId);

        Assert.Equal(JobStatus.Succeeded, launch.Status);
        Assert.True((await rig.StatusAsync(RepoRole.External)).IsDetached);
        Assert.True(rig.Launch.GetSession(rig.Game.Id)!.StartedFromOlderPoint);
    }

    [Fact(Timeout = 180_000)]
    public async Task A_game_started_by_hand_while_the_launch_waits_stops_the_launch_before_it_switches_anything()
    {
        var (rig, vanilla, modded) = await LaunchRig.WithTwoProfilesAsync(followProcessName: true);
        using var _ = rig;
        using var held = new HeldCapture(rig);
        rig.WriteSave("slot3.sav", "three");
        var capture = await rig.Profiles.CaptureAsync(rig.Game.Id, modded.Id, "Third");
        Assert.True(capture.Ok, capture.Error?.Detail);
        Assert.True(held.Reached(rig.Token), "the capture never reached the engine");
        var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla));
        Assert.True(started.Ok, started.Error?.Detail);

        // The player starts the game themselves while the launch waits for the lock.
        var ready = Path.Combine(rig.Workspace.Root, "by-hand-ready.txt");
        var exe = Path.Combine(rig.InstallPath, rig.ExeName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        using var byHand = Process.Start(new ProcessStartInfo(exe, ["--ready-file", ready]) { UseShellExecute = false, WorkingDirectory = rig.InstallPath });
        await FakeGameProcess.WaitForReadyFileAsync(ready);
        held.Release();
        await rig.WaitForJobAsync(capture.Value.JobId);
        var launch = await rig.WaitForJobAsync(started.Value.JobId);

        Assert.Equal(JobStatus.Failed, launch.Status);
        Assert.Equal("error.launch.alreadyRunning", launch.Error?.MessageId);
        Assert.True(rig.InstallHas("mods/a.txt"), "the install folder must still hold the modded state");
    }
}
