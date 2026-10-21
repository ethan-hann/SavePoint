using Savepoint.Core.Errors;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Paths;

/// <summary>
/// Refuses folders that must never be tracked. A path is resolved through symlinks and junctions first,
/// then compared component by component with the platform's two-tier table (never by string prefix).
/// </summary>
public sealed class PathSafety
{
    private readonly PathSyntax _syntax;
    private readonly IPathProbe _probe;
    private readonly List<ParsedPath> _subtreeRoots = [];
    private readonly List<ParsedPath> _exactRoots = [];

    /// <param name="platform">Supplies the table and the OS's path rules.</param>
    /// <param name="extraBlockedRoots">Tier-1 roots on top of the table: SavePoint's own catalog and storage folders.</param>
    /// <param name="probe">
    /// How links are read. By default the real file system when <paramref name="platform"/> is the running OS,
    /// and no links at all for any other OS (its paths do not exist on this machine).
    /// </param>
    public PathSafety(IPlatform platform, IEnumerable<string>? extraBlockedRoots = null, IPathProbe? probe = null)
    {
        _syntax = PathSyntax.For(platform.Os);
        _probe = probe ?? (platform.Os == PlatformFactory.CurrentOs ? PathProbes.Host : PathProbes.None);

        // Resolved once here: every check would otherwise repeat the same dozens of link lookups.
        foreach (var entry in platform.Denylist(extraBlockedRoots ?? []).Entries)
        {
            var root = ParseResolved(entry.Path);
            (entry.Tier == DenylistTier.Subtree ? _subtreeRoots : _exactRoots).Add(root);
        }
    }

    /// <summary>The absolute path with every symlink and junction followed; the same resolution <see cref="Check"/> uses.</summary>
    public string ResolveRealPath(string path) => RealPath.Resolve(path, _syntax, _probe);

    /// <exception cref="PathSafetyException">The path is, or sits inside, a blocked location.</exception>
    /// <exception cref="ArgumentException">The path is not absolute; resolve tokens and check absoluteness first.</exception>
    public void Check(string path)
    {
        var real = ParseResolved(path);

        if (_subtreeRoots.Any(root => _syntax.IsSameOrDescendant(real, root)))
        {
            throw new PathSafetyException(path, 1);
        }

        // A bare root of any volume (a drive nobody listed, a UNC share) is too broad whatever the table says.
        if (real.Parts.Count == 0 || _exactRoots.Any(root => _syntax.IsSame(real, root)))
        {
            throw new PathSafetyException(path, 2);
        }
    }

    /// <summary>True when the two paths are the same folder or one is inside the other, after following links.</summary>
    public bool PathsOverlap(string a, string b)
    {
        var first = ParseResolved(a);
        var second = ParseResolved(b);
        return _syntax.IsSameOrDescendant(first, second) || _syntax.IsSameOrDescendant(second, first);
    }

    private ParsedPath ParseResolved(string path) =>
        _syntax.Split(RealPath.Resolve(path, _syntax, _probe), collapseDots: true);
}
