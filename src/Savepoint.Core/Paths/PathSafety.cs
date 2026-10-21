using System.Net;
using Savepoint.Core.Errors;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Paths;

/// <summary>
/// Refuses folders that must never be tracked. A path is resolved through links, then compared component by component with the
/// platform's table (never by string prefix), and where the OS reports file identities the folder is compared by identity too,
/// which catches a folder reached under another name (SUBST and mapped drives, bind mounts).
/// </summary>
/// <remarks>
/// The table is read once, when this object is built: a new instance is needed after SavePoint's storage root moves, and after
/// a known folder such as Documents is redirected. Tier 1 blocks a path and everything beneath it, and also any folder that
/// contains a tier-1 root; tier 2 blocks exactly the listed folder. The check never writes to the file system.
/// </remarks>
public sealed class PathSafety
{
    private static readonly string[] _macDataVolume = ["System", "Volumes", "Data"];

    private readonly PathSyntax _syntax;
    private readonly IPathProbe _probe;
    private readonly List<TierOneRoot> _subtreeRoots = [];
    private readonly List<ParsedPath> _exactRoots = [];
    private readonly List<ParsedPath> _driveParents = [];
    private readonly HashSet<FileIdentity> _exactIds = [];
    private readonly HashSet<FileIdentity> _tierOneAncestorIds = [];
    private readonly IReadOnlyList<DenylistNameRule> _nameRules;
    private readonly IReadOnlyList<string> _localHostNames;

    /// <summary>
    /// Builds the check for one platform. The platform's catalog, logs, and runtime folders and <paramref name="storageRoot"/>
    /// are always protected as tier 1, on top of the platform's table and <paramref name="extraBlockedRoots"/>.
    /// </summary>
    /// <param name="platform">Supplies the table and the OS's path rules.</param>
    /// <param name="storageRoot">The current storage root; protected even when it has been moved off the platform's default.</param>
    /// <param name="extraBlockedRoots">More tier-1 roots to protect.</param>
    /// <param name="probe">
    /// How links and identities are read. By default the real file system for the running OS, and nothing for any other OS,
    /// whose paths are not on this machine.
    /// </param>
    /// <exception cref="ArgumentException">A root is not an absolute path for the platform's OS.</exception>
    public PathSafety(IPlatform platform, string storageRoot, IEnumerable<string>? extraBlockedRoots = null, IPathProbe? probe = null)
    {
        _syntax = PathSyntax.For(platform.Os);
        _probe = probe ?? (platform.Os == PlatformFactory.CurrentOs ? PathProbes.Host : PathProbes.None);

        // Resolved once here: every check would otherwise repeat the same dozens of link lookups.
        var directories = platform.Directories;
        string[] ownRoots = [directories.CatalogDir, directories.LogsDir, directories.RuntimeDir, storageRoot];
        var table = platform.Denylist([.. ownRoots, .. extraBlockedRoots ?? []]);
        _nameRules = table.NameRules;
        _localHostNames = table.LocalHostNames;
        foreach (var entry in table.Entries)
        {
            var resolved = ParseTableRoot(entry.Path);
            if (entry.Tier == DenylistTier.Subtree)
            {
                AddTierOne(entry, resolved);
            }
            else if (entry.ChildrenOnly)
            {
                _driveParents.Add(resolved);
            }
            else
            {
                _exactRoots.Add(resolved);
                if (IdentityOf(resolved) is { } id)
                {
                    _exactIds.Add(id);
                }
            }
        }
    }

    /// <summary>The absolute path with every symlink and junction followed; the same resolution <see cref="Check"/> uses.</summary>
    /// <exception cref="ArgumentException">The path is not absolute.</exception>
    /// <exception cref="IOException">The path runs into a link loop.</exception>
    public string ResolveRealPath(string path) => RealPath.Resolve(path, _syntax, _probe);

    /// <summary>
    /// Returns when the folder may be tracked. Throws <see cref="PathSafetyException"/> with tier 1 for a protected folder, a folder
    /// inside one, a folder that contains one, a Windows stream spelling, or a network path to this computer or an administrative
    /// share; and with tier 2 for a protected folder itself, a whole mounted drive, or a Steam library folder.
    /// </summary>
    /// <param name="path">An absolute path; resolve tokens and check absoluteness before calling.</param>
    /// <exception cref="ArgumentException">The path is not absolute.</exception>
    /// <exception cref="IOException">The path runs into a link loop.</exception>
    public void Check(string path)
    {
        // A stream spelling reaches a folder under a name no table entry matches, so it is refused before anything else.
        if (_syntax.UsesStreamSyntax(path))
        {
            throw new PathSafetyException(path, 1);
        }

        // \\localhost\C$\... and \\<this computer>\ADMIN$\... are this computer's own folders under another name, and
        // an administrative share ($) is never a place for a game, so neither is resolved at all.
        if (_syntax.TryReadShare(path, out var host, out var share) && (share.EndsWith('$') || IsThisComputer(host)))
        {
            throw new PathSafetyException(path, 1);
        }

        var real = ParseResolved(path);
        var chain = IdentityChain(real);
        var own = chain.Count == real.Parts.Count + 1 ? chain[^1] : (FileIdentity?)null;

        if (_subtreeRoots.Any(root => Blocks(root, real, chain)))
        {
            throw new PathSafetyException(path, 1);
        }

        // A bare root of any volume (a drive nobody listed, a UNC share) is too broad whatever the table says.
        if (real.Parts.Count == 0
            || _exactRoots.Any(root => _syntax.IsSame(real, root))
            || (own is { } exact && _exactIds.Contains(exact))
            || _driveParents.Any(parent => real.Parts.Count == parent.Parts.Count + 1 && _syntax.IsSameOrDescendant(real, parent))
            || _nameRules.Any(rule => EndsWith(real, rule.Names)))
        {
            throw new PathSafetyException(path, 2);
        }

        // Checked last so a folder that is also a tier-2 root keeps that report. Tracking a folder that holds a
        // protected one (SavePoint's own storage) would let a discard or clean delete it. A root that is itself a
        // link counts under both of its names: the folder that holds the link also holds the data it points at.
        if (_subtreeRoots.Any(root => _syntax.IsSameOrDescendant(root.Resolved, real) || _syntax.IsSameOrDescendant(root.Lexical, real))
            || (own is { } holder && _tierOneAncestorIds.Contains(holder)))
        {
            throw new PathSafetyException(path, 1);
        }
    }

    /// <summary>True when the two paths are the same folder or one is inside the other, after following links and comparing identities.</summary>
    /// <exception cref="ArgumentException">A path is not absolute.</exception>
    /// <exception cref="IOException">A path runs into a link loop.</exception>
    public bool PathsOverlap(string a, string b)
    {
        var first = ParseResolved(a);
        var second = ParseResolved(b);
        if (_syntax.IsSameOrDescendant(first, second) || _syntax.IsSameOrDescendant(second, first))
        {
            return true;
        }

        var firstChain = IdentityChain(first);
        var secondChain = IdentityChain(second);
        return (firstChain.Count == first.Parts.Count + 1 && secondChain.Contains(firstChain[^1]))
            || (secondChain.Count == second.Parts.Count + 1 && firstChain.Contains(secondChain[^1]));
    }

    private void AddTierOne(DenylistEntry entry, ParsedPath resolved)
    {
        var lexical = ParseLexical(entry.Path);
        var allowed = (entry.AllowedSubRoots ?? []).Select(ParseTableRoot).ToList();
        _subtreeRoots.Add(new TierOneRoot(
            resolved,
            lexical,
            allowed,
            IdentityOf(resolved),
            [.. allowed.Select(IdentityOf).OfType<FileIdentity>()]));

        // The folders above a root are held by it, under both of its names; their identities are what an alias of a parent matches.
        foreach (var form in new[] { resolved, lexical })
        {
            var chain = IdentityChain(form);
            var existsFully = chain.Count == form.Parts.Count + 1;
            foreach (var id in existsFully ? chain.Take(chain.Count - 1) : chain)
            {
                _tierOneAncestorIds.Add(id);
            }
        }
    }

    // An allowed sub-root and everything beneath it escapes the block; the tier-2 table still judges the sub-root itself.
    // A folder reached by another name is inside a root when one of its ancestors is the root, and outside it again
    // when one of them is an allowed sub-root.
    private bool Blocks(TierOneRoot root, ParsedPath real, IReadOnlyList<FileIdentity> chain)
    {
        if (_syntax.IsSameOrDescendant(real, root.Resolved))
        {
            return !root.Allowed.Any(allowed => _syntax.IsSameOrDescendant(real, allowed));
        }

        return root.Id is { } id && chain.Contains(id) && !root.AllowedIds.Any(chain.Contains);
    }

    private FileIdentity? IdentityOf(ParsedPath path) => _probe.ReadIdentity(_syntax.Join(path.Root, path.Parts));

    // The identity of each existing prefix of the path, root first; it stops at the first part that does not exist.
    private List<FileIdentity> IdentityChain(ParsedPath path)
    {
        var chain = new List<FileIdentity>();
        for (var length = 0; length <= path.Parts.Count; length++)
        {
            var prefix = new ParsedPath(path.Root, path.Parts.Take(length).ToList());
            if (IdentityOf(prefix) is not { } id)
            {
                break;
            }

            chain.Add(id);
        }

        return chain;
    }

    private bool IsThisComputer(string host)
    {
        var name = host.TrimEnd('.');
        if (name.Equals("localhost", StringComparison.OrdinalIgnoreCase) || _localHostNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        // Windows writes an IPv6 address in a network path with '-' for ':' and 's' for '%'.
        const string ipv6Suffix = ".ipv6-literal.net";
        var address = name.EndsWith(ipv6Suffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^ipv6Suffix.Length].Replace('-', ':').Replace('s', '%')
            : name;
        return IPAddress.TryParse(address, out var ip) && IPAddress.IsLoopback(ip);
    }

    private bool EndsWith(ParsedPath path, IReadOnlyList<string> names)
    {
        var skip = path.Parts.Count - names.Count;
        return skip >= 0 && names.Select((name, i) => string.Equals(path.Parts[skip + i], name, _syntax.Comparison)).All(same => same);
    }

    private ParsedPath ParseResolved(string path)
    {
        var parsed = _syntax.Split(RealPath.Resolve(path, _syntax, _probe), collapseDots: true);

        // On macOS the data volume is also reachable as /System/Volumes/Data/X, the same folder as /X.
        return _syntax.Os == OsKind.MacOS && StartsWithFirmlink(parsed.Parts)
            ? new ParsedPath(parsed.Root, [.. parsed.Parts.Skip(_macDataVolume.Length)])
            : parsed;
    }

    private bool StartsWithFirmlink(IReadOnlyList<string> parts) =>
        parts.Count >= _macDataVolume.Length
        && _macDataVolume.Select((name, i) => string.Equals(parts[i], name, _syntax.Comparison)).All(same => same);

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

    private sealed record TierOneRoot(
        ParsedPath Resolved,
        ParsedPath Lexical,
        IReadOnlyList<ParsedPath> Allowed,
        FileIdentity? Id,
        IReadOnlyList<FileIdentity> AllowedIds);
}
