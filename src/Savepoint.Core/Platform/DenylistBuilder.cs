using Savepoint.Core.Paths;

namespace Savepoint.Core.Platform;

/// <summary>Collects table entries, normalizing paths and keeping one entry per path with tier 1 winning.</summary>
internal sealed class DenylistBuilder
{
    private readonly PathSyntax _syntax;
    private readonly Dictionary<string, DenylistEntry> _entries;

    public DenylistBuilder(PathSyntax syntax)
    {
        _syntax = syntax;
        _entries = new Dictionary<string, DenylistEntry>(StringComparer.FromComparison(syntax.Comparison));
    }

    public void Subtree(string path, string reason, IReadOnlyList<string>? allowedSubRoots = null) =>
        Add(path, DenylistTier.Subtree, reason, allowedSubRoots);

    public void Exact(string path, string reason) => Add(path, DenylistTier.ExactOnly, reason, null);

    /// <summary>For optional locations (an environment variable that may be unset).</summary>
    public void ExactIfPresent(string? path, string reason)
    {
        if (path is not null)
        {
            Add(path, DenylistTier.ExactOnly, reason, null);
        }
    }

    public PathDenylist Build() => new([.. _entries.Values]);

    private void Add(string path, DenylistTier tier, string reason, IReadOnlyList<string>? allowedSubRoots)
    {
        var normalized = Normalized(path);
        if (_entries.TryGetValue(normalized, out var existing) && existing.Tier <= tier)
        {
            return;
        }

        _entries[normalized] = new DenylistEntry(normalized, tier, reason, allowedSubRoots?.Select(Normalized).ToList());
    }

    private string Normalized(string path) => _syntax.IsAbsolute(path)
        ? _syntax.Normalize(path)
        : throw new ArgumentException($"'{path}' is not an absolute {_syntax.Os} path.", nameof(path));
}
