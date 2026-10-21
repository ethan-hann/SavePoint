namespace Savepoint.Core.Platform;

/// <summary>The operating systems SavePoint supports; each has its own path rules, tables, and process list.</summary>
public enum OsKind
{
    /// <summary>Windows, with drive letters and network shares.</summary>
    Windows,

    /// <summary>Linux, with XDG folders and /proc.</summary>
    Linux,

    /// <summary>macOS, with Library folders and /Volumes.</summary>
    MacOS,
}
