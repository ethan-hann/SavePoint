namespace Savepoint.Core.Git;

internal enum Presence
{
    Present,
    Gone,
    Unknown,
}

// Whether a listed file is still in the folder. "Gone" is claimed only when the file system says so plainly: a file that cannot be
// looked at, or whose name cannot be told apart from another by reading git's output, stays in the add so git's own answer reaches the player.
internal static class PathPresence
{
    public static Presence Look(string fullPath, Func<string, FileInfo>? open = null)
    {
        // Reading git's output as text turns bytes that are not valid UTF-8 into U+FFFD, so the real file cannot be found by that name.
        if (fullPath.Contains('�'))
        {
            return Presence.Unknown;
        }

        try
        {
            var info = (open ?? (path => new FileInfo(path)))(fullPath);
            return info.Exists || info.LinkTarget is not null ? Presence.Present : Presence.Gone;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Presence.Unknown;
        }
    }
}
