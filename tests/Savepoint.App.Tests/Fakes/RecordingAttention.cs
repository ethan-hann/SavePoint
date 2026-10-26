using Savepoint.App.Platform;

namespace Savepoint.App.Tests.Fakes;

// Remembers which games the launch flow reported as ended.
internal sealed class RecordingAttention : ISessionAttention
{
    public List<string> Ended { get; } = [];

    public void SessionEnded(string gameName) => Ended.Add(gameName);
}
