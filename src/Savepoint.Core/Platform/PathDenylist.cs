namespace Savepoint.Core.Platform;

public enum DenylistTier
{
    /// <summary>The path and every descendant are blocked.</summary>
    Subtree = 1,

    /// <summary>Only the path itself is blocked; folders beneath it are fine.</summary>
    ExactOnly = 2,
}

/// <summary>Reason is for developers and logs; user-facing text comes from the error's message id.</summary>
public sealed record DenylistEntry(string Path, DenylistTier Tier, string Reason);

/// <summary>The paths SavePoint refuses to track, as data. PathSafety does the matching.</summary>
public sealed class PathDenylist
{
    public PathDenylist(IReadOnlyList<DenylistEntry> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<DenylistEntry> Entries { get; }
}
