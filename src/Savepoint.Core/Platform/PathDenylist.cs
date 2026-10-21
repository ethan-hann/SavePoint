namespace Savepoint.Core.Platform;

/// <summary>How far a denylist entry reaches.</summary>
public enum DenylistTier
{
    /// <summary>The path and every descendant are blocked.</summary>
    Subtree = 1,

    /// <summary>Only the path itself is blocked; folders beneath it are fine.</summary>
    ExactOnly = 2,
}

/// <summary>One protected location in a platform's table.</summary>
/// <param name="Path">Absolute path in the platform's own separator style.</param>
/// <param name="Tier">Whether the path and everything beneath it, or only the path, is blocked.</param>
/// <param name="Reason">For developers and logs; user-facing text comes from the error's message id.</param>
/// <param name="AllowedSubRoots">
/// On a <see cref="DenylistTier.Subtree"/> entry: sub-roots, and everything beneath them, that escape the block.
/// The sub-root itself is still judged by the other entries.
/// </param>
/// <param name="ChildrenOnly">
/// On an <see cref="DenylistTier.ExactOnly"/> entry: the entry blocks each direct child folder of <paramref name="Path"/>
/// (a mounted drive under /media) and not <paramref name="Path"/> itself.
/// </param>
public sealed record DenylistEntry(
    string Path,
    DenylistTier Tier,
    string Reason,
    IReadOnlyList<string>? AllowedSubRoots = null,
    bool ChildrenOnly = false);

/// <summary>A folder protected by its name wherever it sits (a Steam library's steamapps folder), as tier 2.</summary>
/// <param name="Names">The last folder names of the path, outermost first; a path ending in exactly these is blocked.</param>
/// <param name="Reason">For developers and logs.</param>
public sealed record DenylistNameRule(IReadOnlyList<string> Names, string Reason);

/// <summary>The paths SavePoint refuses to track, as data. <see cref="Paths.PathSafety"/> does the matching.</summary>
public sealed class PathDenylist
{
    /// <summary>Builds a table from its entries and name rules.</summary>
    /// <param name="entries">The path entries.</param>
    /// <param name="nameRules">Folders protected by name; none when omitted.</param>
    public PathDenylist(IReadOnlyList<DenylistEntry> entries, IReadOnlyList<DenylistNameRule>? nameRules = null)
    {
        Entries = entries;
        NameRules = nameRules ?? [];
    }

    /// <summary>The path entries, tier 1 and tier 2.</summary>
    public IReadOnlyList<DenylistEntry> Entries { get; }

    /// <summary>Folders blocked as tier 2 by name wherever they are.</summary>
    public IReadOnlyList<DenylistNameRule> NameRules { get; }
}
