using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Launch;

// A Steam client a test scripts: closed, open, or one that comes up some looks after it was started.
internal sealed class FakeSteamClient : ISteamClient
{
    private int _starts;
    private int _looksSinceStart;
    private volatile bool _ready;

    public FakeSteamClient(bool ready = false)
    {
        _ready = ready;
    }

    // After a start, the client is ready once it has been looked at this many times; null for a client that never comes up.
    public int? ReadyAfterLooks { get; init; }

    // What starting the client does to the machine, for a stand-in game that looks at it.
    public Action? OnStart { get; init; }

    // False for a client that cannot be found or started.
    public bool CanStart { get; init; } = true;

    public int Starts => Volatile.Read(ref _starts);

    public bool IsReady()
    {
        if (!_ready && Starts > 0 && ReadyAfterLooks is { } looks && Interlocked.Increment(ref _looksSinceStart) > looks)
        {
            _ready = true;
        }

        return _ready;
    }

    public bool TryStart()
    {
        Interlocked.Increment(ref _starts);
        OnStart?.Invoke();
        return CanStart;
    }
}
