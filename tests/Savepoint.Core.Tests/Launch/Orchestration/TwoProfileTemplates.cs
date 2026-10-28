using System.Collections.Concurrent;
using Savepoint.Core.Tests.Backend;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A world a LaunchRig starts from (a game with its profiles, taken with real git) is built once and copied for each test. A test that
// starts the stand-in game finds it by the program's file name, which the profiles' snapshots hold, so two tests that run at the same
// time cannot share a name. A slot is one built world with its own name; a test rents a slot for its whole life and a new one is built
// only when every existing slot of that world is in use, so the number built follows the tests' parallelism, not their count. Each
// world has a key, and the slots of one key all hold the same world.
internal static class TwoProfileTemplates
{
    internal const string TwoProfilesKey = "two-profiles";

    private static readonly ConcurrentDictionary<string, ConcurrentStack<Slot>> _free = new();
    private static readonly object _gate = new();
    private static readonly List<string> _roots = [];

    static TwoProfileTemplates()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteAll();
    }

    // A slot no other test holds. A build that fails or is cancelled with its test leaves nothing behind for the next one.
    public static async Task<Slot> RentAsync(string key, Func<LaunchRig, Task> build)
    {
        if (_free.GetOrAdd(key, _ => new ConcurrentStack<Slot>()).TryPop(out var free))
        {
            return free;
        }

        var rig = await LaunchRig.CreateAsync();
        try
        {
            await build(rig);

            // A world built by playing leaves no program running, so no file of the template is held when it is copied.
            rig.KillGame();
            SpinWait.SpinUntil(() => rig.GameProcessIds().Count == 0, TimeSpan.FromSeconds(10));

            // The file says the game it was written for is running, and every copy would carry it.
            File.Delete(rig.ReadyFile);
            await rig.Workspace.WaitForIdleAsync();
        }
        catch
        {
            rig.Dispose();
            throw;
        }

        lock (_gate)
        {
            _roots.Add(rig.Workspace.Root);
        }

        return new Slot(key, rig.Workspace, rig.ExeName);
    }

    public static Task<Slot> RentAsync() => RentAsync(TwoProfilesKey, rig => rig.BuildTwoProfilesAsync());

    public static void Return(Slot slot) => _free.GetOrAdd(slot.Key, _ => new ConcurrentStack<Slot>()).Push(slot);

    private static void DeleteAll()
    {
        lock (_gate)
        {
            foreach (var root in _roots)
            {
                Savepoint.Core.Tests.Git.GitWorkspace.DeleteDirectory(root);
            }

            _roots.Clear();
        }
    }

    // A built world that is only ever copied from, and the program name its snapshots hold.
    internal sealed record Slot(string Key, BackendWorkspace Workspace, string ExeName);
}
