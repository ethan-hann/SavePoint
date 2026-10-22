using System.Diagnostics;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A discard takes the game's lock like every other write, so it never runs in the middle of a capture of the same game (D13.14);
// a preview is a read and takes no game lock, so a capture never holds it up.
public class LaunchGameLockTests
{
    private static readonly TimeSpan _held = TimeSpan.FromSeconds(20);

    // Holds the next capture at its first free-space question, with the game's lock, until released.
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

    [Fact(Timeout = 120_000)]
    public async Task A_discard_waits_for_a_capture_of_the_same_game_instead_of_running_beside_it()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");
        using var held = new HeldCapture(rig);
        rig.WriteSave("slot2.sav", "a second save");
        rig.WriteInstall("mods/c.txt", "an unsaved mod");

        // The capture holds the game's lock, stopped before it writes anything.
        var capture = await rig.Profiles.CaptureAsync(rig.Game.Id, vanilla.Id, "Second", [RepoRole.External]);
        Assert.True(capture.Ok, capture.Error?.Detail);
        Assert.True(held.Reached(rig.Token), "the capture never reached the engine");

        var discard = rig.Launch.DiscardChangesAsync(rig.Game.Id, RepoRole.Install);
        var early = await Task.WhenAny(discard, Task.Delay(500, rig.Token));

        Assert.NotSame(discard, early);
        Assert.True(rig.InstallHas("mods/c.txt"), "the discard must not run while the capture holds the game");

        held.Release();
        Assert.True((await discard).Ok);
        await rig.WaitForJobAsync(capture.Value.JobId);
        Assert.False(rig.InstallHas("mods/c.txt"));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_preview_is_answered_while_a_capture_of_the_same_game_holds_its_lock()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");
        using var held = new HeldCapture(rig);
        rig.WriteSave("slot2.sav", "a second save");
        var capture = await rig.Profiles.CaptureAsync(rig.Game.Id, vanilla.Id, "Second", [RepoRole.External]);
        Assert.True(capture.Ok, capture.Error?.Detail);
        Assert.True(held.Reached(rig.Token), "the capture never reached the engine");

        var preview = rig.Launch.GetPreviewAsync(rig.Game.Id, new LaunchTarget.ProfileTip(vanilla.Id), rig.Token);
        var first = await Task.WhenAny(preview, Task.Delay(TimeSpan.FromSeconds(10), rig.Token));

        Assert.Same(preview, first);
        Assert.True((await preview).Ok);
        held.Release();
        await rig.WaitForJobAsync(capture.Value.JobId);
    }
}
