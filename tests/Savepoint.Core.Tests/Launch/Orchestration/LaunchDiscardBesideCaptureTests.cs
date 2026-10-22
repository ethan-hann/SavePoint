using System.Diagnostics;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A discard takes the game's lock like every other write, so it never runs in the middle of a capture of the same game (D13.14).
public class LaunchDiscardBesideCaptureTests
{
    private static readonly TimeSpan _held = TimeSpan.FromSeconds(20);

    [Fact(Timeout = 120_000)]
    public async Task A_discard_waits_for_a_capture_of_the_same_game_instead_of_running_beside_it()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        rig.Workspace.Disk.FreePerPath = path =>
        {
            if (path.StartsWith(rig.Workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase)
                && new StackTrace().ToString().Contains("CaptureCoreAsync", StringComparison.Ordinal))
            {
                reached.Set();
                release.Wait(_held);
            }

            return null;
        };
        rig.WriteSave("slot2.sav", "a second save");
        rig.WriteInstall("mods/c.txt", "an unsaved mod");
        try
        {
            // The capture holds the game's lock, stopped before it writes anything.
            var capture = await rig.Profiles.CaptureAsync(rig.Game.Id, vanilla.Id, "Second", [RepoRole.External]);
            Assert.True(capture.Ok, capture.Error?.Detail);
            Assert.True(reached.Wait(_held, rig.Token), "the capture never reached the engine");

            var discard = rig.Launch.DiscardChangesAsync(rig.Game.Id, RepoRole.Install);
            var early = await Task.WhenAny(discard, Task.Delay(500, rig.Token));

            Assert.NotSame(discard, early);
            Assert.True(rig.InstallHas("mods/c.txt"), "the discard must not run while the capture holds the game");

            release.Set();
            Assert.True((await discard).Ok);
            await rig.WaitForJobAsync(capture.Value.JobId);
            Assert.False(rig.InstallHas("mods/c.txt"));
        }
        finally
        {
            release.Set();
        }
    }
}
