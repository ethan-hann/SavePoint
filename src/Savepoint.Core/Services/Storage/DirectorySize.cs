namespace Savepoint.Core.Services.Storage;

/// <summary>Adds up the size of the files under a folder.</summary>
public static class DirectorySize
{
    // Hidden and system entries are real bytes on disk; the default options would skip them.
    private static readonly EnumerationOptions _everything = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
    };

    /// <summary>Totals the bytes of every file under <paramref name="path"/>, hidden ones included.</summary>
    /// <param name="path">The folder to measure.</param>
    /// <param name="cancellationToken">Stops the scan; the scan checks it between entries.</param>
    /// <returns>
    /// The total in bytes. A missing folder, an unreadable subfolder, and a file that vanishes mid-scan count as zero
    /// instead of failing. Links (symbolic links and junctions) are skipped, never followed, so a link cannot double-count
    /// or escape the folder.
    /// </returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <remarks>Runs on the thread pool.</remarks>
    public static Task<long> GetBytesAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Measure(path, static file => file.Length, cancellationToken), cancellationToken);

    // lengthOf is a seam: a test can remove a file between the listing and the size read.
    internal static long Measure(string path, Func<FileInfo, long> lengthOf, CancellationToken cancellationToken)
    {
        var total = 0L;
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            try
            {
                foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", _everything))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }

                    if (entry is FileInfo file)
                    {
                        total += LengthOrZero(file, lengthOf);
                    }
                    else
                    {
                        pending.Push(entry.FullName);
                    }
                }
            }
            catch (Exception e) when (e is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
            {
                // A folder that was removed or is off limits adds nothing; the scan carries on with the rest.
            }
        }

        return total;
    }

    private static long LengthOrZero(FileInfo file, Func<FileInfo, long> lengthOf)
    {
        try
        {
            return lengthOf(file);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            return 0;
        }
    }
}
