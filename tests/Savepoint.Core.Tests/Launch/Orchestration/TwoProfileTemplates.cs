using System.Collections.Concurrent;
using Savepoint.Core.Tests.Backend;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// The world LaunchRig.WithTwoProfilesAsync starts from (a game with two profiles, taken with real git) is built once and copied for each
// test. A test that starts the stand-in game finds it by the program's file name, which the profiles' snapshots hold, so two tests that
// run at the same time cannot share a name. A slot is one built world with its own name; a test rents a slot for its whole life and a
// new one is built only when every existing slot is in use, so the number built follows the tests' parallelism, not their count.
internal static class TwoProfileTemplates
{
    private static readonly ConcurrentStack<Slot> _free = new();
    private static readonly object _gate = new();
    private static readonly List<string> _roots = [];

    static TwoProfileTemplates()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteAll();
    }

    // A slot no other test holds. A build that fails or is cancelled with its test leaves nothing behind for the next one.
    public static async Task<Slot> RentAsync()
    {
        if (_free.TryPop(out var free))
        {
            return free;
        }

        var rig = await LaunchRig.CreateAsync();
        try
        {
            await rig.BuildTwoProfilesAsync();
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

        return new Slot(rig.Workspace, rig.ExeName);
    }

    public static void Return(Slot slot) => _free.Push(slot);

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
    internal sealed record Slot(BackendWorkspace Workspace, string ExeName);
}
