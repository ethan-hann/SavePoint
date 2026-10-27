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

    /// <summary>Names the volume that holds <paramref name="path"/>, so that folders which share one can be told from folders which do not.</summary>
    /// <param name="path">A file or folder, existing or not; the nearest existing ancestor decides the volume.</param>
    /// <returns>A key that is equal for two paths on the same volume and differs between volumes, or null when the volume cannot be determined (the folder is then judged on its own).</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty or blank.</exception>
    string? GetVolumeId(string path);
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

    /// <inheritdoc/>
    public string? GetVolumeId(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (ExistingFolderOf(path) is not { } folder)
        {
            return null;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                // A UNC share has no drive root to compare.
                var root = Path.GetPathRoot(folder)!;
                return root.StartsWith(@"\\", StringComparison.Ordinal) ? null : root.ToUpperInvariant();
            }

            // The mount that holds the folder is the deepest one whose path is a prefix of it.
            return DriveInfo.GetDrives()
                .Select(drive => Path.TrimEndingDirectorySeparator(drive.RootDirectory.FullName))
                .Where(mount => folder == mount || folder.StartsWith(mount.EndsWith('/') ? mount : mount + '/', StringComparison.Ordinal))
                .OrderByDescending(mount => mount.Length)
                .FirstOrDefault();
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ExistingFolderOf(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return NearestExistingFolder(full);
    }

    private static long? Measure(string path, Func<DriveInfo, long> read)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var folder = ExistingFolderOf(path);
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
