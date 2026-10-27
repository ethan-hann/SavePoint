using Savepoint.App.Platform;

namespace Savepoint.App.Tests.Features.Games;

// A window that is in front until a test sends it to the back; raises Changed the way the real tracker does.
internal sealed class FakeWindowActivity : IWindowActivitySource
{
    public WindowActivity Current { get; private set; } = new(true, false);

    public event EventHandler? Changed;

    public void Become(bool active, bool minimized = false)
    {
        Current = new WindowActivity(active, minimized);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
