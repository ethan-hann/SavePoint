using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Platform;

// A platform that answers like another one but with the app folders a test chooses.
internal sealed class RelocatedPlatform(IPlatform inner, AppDirectories directories) : IPlatform
{
    public OsKind Os => inner.Os;

    public StringComparison PathComparison => inner.PathComparison;

    public AppDirectories Directories => directories;

    public IReadOnlyDictionary<string, string> PathTokens => inner.PathTokens;

    public IProcessInspector Processes => inner.Processes;

    public ISteamClient Steam => inner.Steam;

    public PathDenylist Denylist(IEnumerable<string> extraBlockedRoots) => inner.Denylist(extraBlockedRoots);
}
