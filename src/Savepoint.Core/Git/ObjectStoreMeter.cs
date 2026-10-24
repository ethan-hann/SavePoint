namespace Savepoint.Core.Git;

// Measures how many bytes a repository's object store holds right now, including files git is still writing.
internal static class ObjectStoreMeter
{
    private static readonly EnumerationOptions _all = new() { RecurseSubdirectories = true, IgnoreInaccessible = true };

    public static long Size(string objectsDirectory)
    {
        var directory = new DirectoryInfo(objectsDirectory);
        long total = 0;
        try
        {
            foreach (var file in directory.EnumerateFiles("*", _all))
            {
                total += file.Name.StartsWith("tmp_", StringComparison.Ordinal) ? OpenLength(file) : file.Length;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A folder git removed while it was being read: what was counted so far still stands.
        }

        return total;
    }

    // On Windows a file's directory entry is not updated while another process keeps it open, so a file git is writing reads as
    // empty there; asking the open file itself gives its real length.
    private static long OpenLength(FileInfo file)
    {
        try
        {
            using var handle = File.OpenHandle(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return RandomAccess.GetLength(handle);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Renamed or removed as git finished with it, or held so that it cannot be opened: the directory's figure is the best there is.
            return file.Length;
        }
    }
}
