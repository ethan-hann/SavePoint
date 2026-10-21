namespace Savepoint.Core.Launch;

/// <summary>Answers from the real file system.</summary>
internal sealed class SystemLaunchFiles : ILaunchFiles
{
    private const UnixFileMode _anyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public bool HasExecuteBit(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            return (File.GetUnixFileMode(path) & _anyExecute) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable mode: let the start attempt say what is wrong.
            return true;
        }
    }
}
