using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

/// <summary>Why SavePoint cannot read a file that git lists as changed.</summary>
public enum HeldFileKind
{
    /// <summary>Another program holds the file open without sharing it, which only Windows enforces: a sharing or lock violation.</summary>
    InUse,

    /// <summary>The file cannot be read at all: access is denied, or the read fails some other way. Closing programs does not help.</summary>
    Unreadable,
}

/// <summary>
/// A file git could not read, so it reports it as changed whether or not anyone changed it. When every change in a folder is such a
/// file, the folder has no unsaved work, and the file is what stands in the way instead.
/// </summary>
/// <param name="Path">The full path of the file.</param>
/// <param name="Kind">Whether it is held by a program or cannot be read.</param>
public sealed record HeldFile(string Path, HeldFileKind Kind)
{
    /// <summary>The error a switch or a save that has to read this file stops with.</summary>
    public SavepointException ToException() => Kind == HeldFileKind.InUse
        ? new GitOperationException(Path, $"'{Path}' is in use by another program, so the folder could not be switched.")
        : new FileUnreadableException(Path);
}
