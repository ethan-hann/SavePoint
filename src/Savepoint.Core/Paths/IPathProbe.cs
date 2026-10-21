namespace Savepoint.Core.Paths;

/// <summary>The file-system questions real-path resolution and path safety ask, so they can run against a fake.</summary>
public interface IPathProbe
{
    /// <summary>The raw target text when <paramref name="path"/> is a symlink or junction (even a dangling one), else null.</summary>
    string? ReadLinkTarget(string path);

    /// <summary>The on-disk long name of the last component of an existing path that uses a short (8.3) name, else null.</summary>
    string? ExpandShortName(string path);

    /// <summary>
    /// The identity of the existing file or folder at <paramref name="path"/>, with links followed. Null when it does not exist,
    /// cannot be opened, or the OS offers no identity (only Windows reports one).
    /// </summary>
    FileIdentity? ReadIdentity(string path);
}
