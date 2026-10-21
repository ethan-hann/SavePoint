using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Paths;

/// <summary>An in-memory link table, keyed by canonical path text, so link logic runs on any host.</summary>
internal sealed class FakePathProbe : IPathProbe
{
    private readonly Dictionary<string, string> _links;
    private readonly Dictionary<string, string> _shortNames;

    public FakePathProbe(PathSyntax syntax)
    {
        var comparer = syntax.Comparison == StringComparison.Ordinal ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        _links = new Dictionary<string, string>(comparer);
        _shortNames = new Dictionary<string, string>(comparer);
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

    public string? ReadLinkTarget(string path) => _links.GetValueOrDefault(path);

    public string? ExpandShortName(string path) => _shortNames.GetValueOrDefault(path);
}
