namespace Savepoint.Core.Git;

// Tells a file another program holds open from a file that was merely edited: git reports both as "local changes".
internal static class FileLockProbe
{
    // True when the file exists but cannot be opened for reading even by a reader that allows every kind of sharing.
    public static bool IsLocked(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
