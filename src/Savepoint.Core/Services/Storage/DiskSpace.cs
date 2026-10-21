namespace Savepoint.Core.Services.Storage;

/// <summary>Reports free space on the volume that holds a path, so a write can be refused before it starts (D13.16).</summary>
public interface IDiskSpace
{
    /// <summary>Gets the bytes a normal user can still write on the volume that holds <paramref name="path"/>.</summary>
    /// <param name="path">A file or folder, existing or not; the nearest existing ancestor decides the volume.</param>
    /// <returns>The free bytes, or null when the volume cannot be determined (no such drive, a network share, an unreadable path).</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty or blank.</exception>
    long? GetFreeBytes(string path);
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
    public long? GetFreeBytes(string path)
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
            var drive = new DriveInfo(OperatingSystem.IsWindows() ? Path.GetPathRoot(folder)! : folder);
            return drive.AvailableFreeSpace;
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
