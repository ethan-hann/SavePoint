using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Paths;

/// <summary>An in-memory link and identity table, keyed by canonical path text, so link logic runs on any host.</summary>
internal sealed class FakePathProbe : IPathProbe
{
    private readonly Dictionary<string, string> _links;
    private readonly Dictionary<string, string> _shortNames;
    private readonly Dictionary<string, FileIdentity> _identities;
    private readonly Dictionary<string, string[]> _finalPaths;

    public FakePathProbe(PathSyntax syntax)
    {
        var comparer = syntax.Comparison == StringComparison.Ordinal ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        _finalPaths = new Dictionary<string, string[]>(comparer);
        _links = new Dictionary<string, string>(comparer);
        _shortNames = new Dictionary<string, string>(comparer);
        _identities = new Dictionary<string, FileIdentity>(comparer);
    }

    public FakePathProbe Link(string path, string target)
    {
        _links[path] = target;
        return this;
    }

    public FakePathProbe ShortName(string path, string longName)
    {
        _shortNames[path] = longName;
        return this;
    }

    /// <summary>The folder at <paramref name="path"/> exists with this identity; folders that are never named here do not exist.</summary>
    public FakePathProbe Identity(string path, ulong volume, ulong id)
    {
        _identities[path] = new FileIdentity(volume, id);
        return this;
    }

    /// <summary>The existing folder at <paramref name="path"/> is really at these names (what the OS reports as its final path).</summary>
    public FakePathProbe FinalPath(string path, params string[] names)
    {
        _finalPaths[path] = names;
        return this;
    }

    public IReadOnlyList<string> ReadFinalPaths(string path) => _finalPaths.GetValueOrDefault(path) ?? [];

    public string? ReadLinkTarget(string path) => _links.GetValueOrDefault(path);

    public string? ExpandShortName(string path) => _shortNames.GetValueOrDefault(path);

    public FileIdentity? ReadIdentity(string path) => _identities.TryGetValue(path, out var identity) ? identity : null;
}
