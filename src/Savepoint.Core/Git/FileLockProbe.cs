namespace Savepoint.Core.Git;

// Tells a file git cannot read from a file that was merely edited: git reports both as "local changes".
internal static class FileLockProbe
{
    // Windows' own numbers for "another program has the file open": ERROR_SHARING_VIOLATION and ERROR_LOCK_VIOLATION.
    private const int _sharingViolation = 32;
    private const int _lockViolation = 33;

    // Null when the file can be read, even by a reader that allows every kind of sharing (or is not there). Only a sharing or lock
    // violation means a program holds it; access denied and any other failure to read it are a different problem, which closing
    // programs does not fix.
    public static HeldFileKind? Probe(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return null;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return HeldFileKind.Unreadable;
        }
        catch (IOException e)
        {
            return (e.HResult & 0xFFFF) is _sharingViolation or _lockViolation ? HeldFileKind.InUse : HeldFileKind.Unreadable;
        }
    }
}
