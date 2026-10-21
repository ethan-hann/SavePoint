namespace Savepoint.Core.Platform;

/// <summary>Everything that differs by OS: where SavePoint keeps its data, path tokens, what is too dangerous to track, and the process list.</summary>
public interface IPlatform
{
    /// <summary>Which OS this platform describes.</summary>
    OsKind Os { get; }

    /// <summary>OrdinalIgnoreCase on Windows and macOS, Ordinal on Linux.</summary>
    StringComparison PathComparison { get; }

    /// <summary>SavePoint's own folders on this OS.</summary>
    AppDirectories Directories { get; }

    /// <summary>Token name (without braces) to its folder. A token the OS lacks is simply absent.</summary>
    IReadOnlyDictionary<string, string> PathTokens { get; }

    /// <summary>The tier-1 and tier-2 tables; <paramref name="extraBlockedRoots"/> become tier-1 entries (SavePoint's own folders).</summary>
    /// <param name="extraBlockedRoots">Absolute paths in this OS's style; a relative one throws <see cref="ArgumentException"/>.</param>
    PathDenylist Denylist(IEnumerable<string> extraBlockedRoots);

    /// <summary>Lists the running processes.</summary>
    IProcessInspector Processes { get; }
}
