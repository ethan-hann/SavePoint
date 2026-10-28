using Savepoint.App.Features.Common;

namespace Savepoint.App.Tests.Fakes;

// Runs idle work only when a test says so, and optionally holds back ordinary posts too, so the order of events is the test's to pick.
// Every rig and harness takes one in place of the immediate dispatcher, to show a screen what the real dispatcher would do to it.
internal sealed class ManualDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _posted = [];
    private readonly Queue<Action> _idle = [];

    public bool PostsImmediately { get; set; } = true;

    public int PostedPending => _posted.Count;

    public int IdlePending => _idle.Count;

    public void Post(Action action)
    {
        if (PostsImmediately)
        {
            action();
        }
        else
        {
            _posted.Enqueue(action);
        }
    }

    public void PostWhenIdle(Action action) => _idle.Enqueue(action);

    public void RunPosted()
    {
        while (_posted.TryDequeue(out var action))
        {
            action();
        }
    }

    public void RunIdle()
    {
        while (_idle.TryDequeue(out var action))
        {
            action();
        }
    }

    // Everything waiting, ordinary posts first and then idle work, until nothing is left.
    public void RunAll()
    {
        while (_posted.Count > 0 || _idle.Count > 0)
        {
            RunPosted();
            RunIdle();
        }
    }
}
