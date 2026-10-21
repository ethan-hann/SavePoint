namespace Savepoint.Core.Services.Storage;

/// <summary>Reports free space on the volume that holds a path, so a write can be refused before it starts (D13.16).</summary>
public interface IDiskSpace
{
    /// <summary>Gets the bytes a normal user can still write on the volume that holds <paramref name="path"/>.</summary>
    /// <param name="path">A file or folder, existing or not; the nearest existing ancestor decides the volume.</param>
    /// <returns>The free bytes, or null when the volume cannot be determined (no such drive, a network share, an unreadable path).</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty or blank.</exception>
    long? GetFreeBytes(string path);

    /// <summary>Gets the size of the volume that holds <paramref name="path"/>, which the storage summary shows beside the free space.</summary>
    /// <param name="path">A file or folder, existing or not; the nearest existing ancestor decides the volume.</param>
    /// <returns>The total bytes, or null when the volume cannot be determined.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty or blank.</exception>
    long? GetTotalBytes(string path);
}

/// <summary>The real <see cref="IDiskSpace"/>, backed by <see cref="DriveInfo"/>.</summary>
/// <remarks>
/// The figure is what the volume offers the current user (quotas and reserved blocks excluded), read at the time of the
/// call and stale a moment later. On Windows a volume mounted into a folder reads as the drive letter that holds the
/// folder; UNC paths read as unknown.
/// </remarks>
public sealed class DiskSpace : IDiskSpace
{
    /// <inheritdoc/>
    public long? GetFreeBytes(string path) => Measure(path, static drive => drive.AvailableFreeSpace);

    /// <inheritdoc/>
    public long? GetTotalBytes(string path) => Measure(path, static drive => drive.TotalSize);

    private static long? Measure(string path, Func<DriveInfo, long> read)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var folder = NearestExistingFolder(full);
        if (folder is null)
        {
            return null;
        }

        try
        {
            // Unix asks the file system about the path itself; Windows reads the drive from the path root.
            return read(new DriveInfo(OperatingSystem.IsWindows() ? Path.GetPathRoot(folder)! : folder));
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? NearestExistingFolder(string fullPath)
    {
        for (var current = fullPath; current is not null; current = Path.GetDirectoryName(current))
        {
            if (Directory.Exists(current))
            {
                return current;
            }
        }

        return null;
    }
}
