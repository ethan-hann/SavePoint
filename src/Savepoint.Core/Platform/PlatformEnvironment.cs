using Savepoint.Core.Platform.Windows;

namespace Savepoint.Core.Platform;

/// <summary>
/// Everything a platform reads from the machine, so each OS's tables can be built from a fake on any host.
/// </summary>
public sealed record PlatformEnvironment
{
    public required string HomeDirectory { get; init; }

    public Func<string, string?> GetVariable { get; init; } = static _ => null;

    public Func<KnownFolder, string?> GetKnownFolder { get; init; } = static _ => null;

    public Func<IReadOnlyList<string>> ListDriveRoots { get; init; } = static () => [];

    /// <summary>Reads a small text file (the Linux user-dirs.dirs); null when it is missing or unreadable.</summary>
    public Func<string, string?> ReadTextFile { get; init; } = static _ => null;

    public static PlatformEnvironment FromCurrentMachine()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetEnvironmentVariable("HOME")
                ?? throw new InvalidOperationException("This account has no home directory.");
        }

        return new PlatformEnvironment
        {
            HomeDirectory = home,
            GetVariable = name => Environment.GetEnvironmentVariable(name),
            GetKnownFolder = WindowsKnownFolders.Get,
            ListDriveRoots = ListDrives,
            ReadTextFile = TryReadTextFile,
        };
    }

    private static IReadOnlyList<string> ListDrives()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            return [.. DriveInfo.GetDrives().Select(drive => drive.Name)];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static string? TryReadTextFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
