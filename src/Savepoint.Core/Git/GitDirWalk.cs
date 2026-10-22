namespace Savepoint.Core.Git;

// How the engine walks a git directory: never through a link (a junction or a symbolic link). A repository can gain one after it was
// adopted, and git itself writes refs and objects through it, so a walk that follows one reads, clears or deletes files outside the folder.
internal static class GitDirWalk
{
    /// <summary>Options for enumerating the files of a git directory: links are neither listed nor entered.</summary>
    public static EnumerationOptions Files(bool recurse) => new() { RecurseSubdirectories = recurse, AttributesToSkip = FileAttributes.ReparsePoint };

    /// <summary>Options for enumerating the folders of a git directory: links are neither listed nor entered.</summary>
    public static EnumerationOptions Folders() => new() { AttributesToSkip = FileAttributes.ReparsePoint };

    /// <summary>True for a folder that exists and is not a link.</summary>
    public static bool IsRealFolder(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
