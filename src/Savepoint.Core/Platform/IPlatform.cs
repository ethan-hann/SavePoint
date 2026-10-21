namespace Savepoint.Core.Platform;

public interface IPlatform
{
    OsKind Os { get; }

    /// <summary>OrdinalIgnoreCase on Windows and macOS, Ordinal on Linux.</summary>
    StringComparison PathComparison { get; }

    AppDirectories Directories { get; }

    /// <summary>Token name (without braces) to its folder. A token the OS lacks is simply absent.</summary>
    IReadOnlyDictionary<string, string> PathTokens { get; }

    /// <summary>The tier-1 and tier-2 tables; <paramref name="extraBlockedRoots"/> become tier-1 entries (SavePoint's own folders).</summary>
    PathDenylist Denylist(IEnumerable<string> extraBlockedRoots);

    IProcessInspector Processes { get; }
}
