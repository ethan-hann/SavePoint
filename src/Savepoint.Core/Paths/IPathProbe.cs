namespace Savepoint.Core.Paths;

/// <summary>The file-system questions real-path resolution asks, so it can run against a fake.</summary>
public interface IPathProbe
{
    /// <summary>The raw target text when <paramref name="path"/> is a symlink or junction (even a dangling one), else null.</summary>
    string? ReadLinkTarget(string path);

    /// <summary>The on-disk long name of the last component of an existing path that uses a short (8.3) name, else null.</summary>
    string? ExpandShortName(string path);
}
