using Savepoint.Core.Errors;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Paths;

/// <summary>Resolves a path through links, then compares it component by component with the table (never by string prefix).</summary>
public sealed class PathSafety
{
    private readonly PathSyntax _syntax;
    private readonly IPathProbe _probe;
    private readonly List<TierOneRoot> _subtreeRoots = [];
    private readonly List<ParsedPath> _exactRoots = [];

    /// <summary>The probe defaults to the real file system for the running OS and to no links for any other OS, whose paths are not on this machine.</summary>
    public PathSafety(IPlatform platform, string storageRoot, IEnumerable<string>? extraBlockedRoots = null, IPathProbe? probe = null)
    {
        _syntax = PathSyntax.For(platform.Os);
        _probe = probe ?? (platform.Os == PlatformFactory.CurrentOs ? PathProbes.Host : PathProbes.None);

        // Resolved once here: every check would otherwise repeat the same dozens of link lookups.
        var directories = platform.Directories;
        string[] ownRoots = [directories.CatalogDir, directories.LogsDir, directories.RuntimeDir, storageRoot];
        foreach (var entry in platform.Denylist([.. ownRoots, .. extraBlockedRoots ?? []]).Entries)
        {
            if (entry.Tier == DenylistTier.Subtree)
            {
                _subtreeRoots.Add(new TierOneRoot(
                    ParseTableRoot(entry.Path),
                    ParseLexical(entry.Path),
                    [.. (entry.AllowedSubRoots ?? []).Select(ParseTableRoot)]));
            }
            else
            {
                _exactRoots.Add(ParseTableRoot(entry.Path));
            }
        }
    }

    /// <summary>The absolute path with every symlink and junction followed; the same resolution <see cref="Check"/> uses.</summary>
    public string ResolveRealPath(string path) => RealPath.Resolve(path, _syntax, _probe);

    /// <summary>Throws PathSafetyException for a blocked location; the path must already be absolute (ArgumentException otherwise).</summary>
    public void Check(string path)
    {
        // A stream spelling reaches a folder under a name no table entry matches, so it is refused before anything else.
        if (_syntax.UsesStreamSyntax(path))
        {
            throw new PathSafetyException(path, 1);
        }

        var real = ParseResolved(path);

        if (_subtreeRoots.Any(root => Blocks(root, real)))
        {
            throw new PathSafetyException(path, 1);
        }

        // A bare root of any volume (a drive nobody listed, a UNC share) is too broad whatever the table says.
        if (real.Parts.Count == 0 || _exactRoots.Any(root => _syntax.IsSame(real, root)))
        {
            throw new PathSafetyException(path, 2);
        }

        // Checked last so a folder that is also a tier-2 root keeps that report. Tracking a folder that holds a
        // protected one (SavePoint's own storage) would let a discard or clean delete it. A root that is itself a
        // link counts under both of its names: the folder that holds the link also holds the data it points at.
        if (_subtreeRoots.Any(root => _syntax.IsSameOrDescendant(root.Resolved, real) || _syntax.IsSameOrDescendant(root.Lexical, real)))
        {
            throw new PathSafetyException(path, 1);
        }
    }

    /// <summary>True when the two paths are the same folder or one is inside the other, after following links.</summary>
    public bool PathsOverlap(string a, string b)
    {
        var first = ParseResolved(a);
        var second = ParseResolved(b);
        return _syntax.IsSameOrDescendant(first, second) || _syntax.IsSameOrDescendant(second, first);
    }

    // An allowed sub-root and everything beneath it escapes the block; the tier-2 table still judges the sub-root itself.
    private bool Blocks(TierOneRoot root, ParsedPath real) =>
        _syntax.IsSameOrDescendant(real, root.Resolved) && !root.Allowed.Any(allowed => _syntax.IsSameOrDescendant(real, allowed));

    private ParsedPath ParseResolved(string path) =>
        _syntax.Split(RealPath.Resolve(path, _syntax, _probe), collapseDots: true);

    private ParsedPath ParseLexical(string path) => _syntax.Split(path, collapseDots: true);

    // A link loop at a table root must not stop the guard from being built; the root keeps its own name.
    private ParsedPath ParseTableRoot(string path)
    {
        try
        {
            return ParseResolved(path);
        }
        catch (IOException)
        {
            return ParseLexical(path);
        }
    }

    private sealed record TierOneRoot(ParsedPath Resolved, ParsedPath Lexical, IReadOnlyList<ParsedPath> Allowed);
}
