namespace Savepoint.Core.Platform;

public enum DenylistTier
{
    /// <summary>The path and every descendant are blocked.</summary>
    Subtree = 1,

    /// <summary>Only the path itself is blocked; folders beneath it are fine.</summary>
    ExactOnly = 2,
}

/// <summary>Reason is for developers and logs; a tier-1 entry's AllowedSubRoots (and everything beneath them) escape its block.</summary>
public sealed record DenylistEntry(string Path, DenylistTier Tier, string Reason, IReadOnlyList<string>? AllowedSubRoots = null);

/// <summary>The paths SavePoint refuses to track, as data. PathSafety does the matching.</summary>
public sealed class PathDenylist
{
    public PathDenylist(IReadOnlyList<DenylistEntry> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<DenylistEntry> Entries { get; }
}
