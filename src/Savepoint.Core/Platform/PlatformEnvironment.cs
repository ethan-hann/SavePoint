using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Savepoint.Core.Artwork;
using Savepoint.Core.Platform.Windows;

namespace Savepoint.Core.Platform;

/// <summary>Everything a platform reads from the machine, so each OS's tables can be built from a fake on any host.</summary>
public sealed record PlatformEnvironment
{
    /// <summary>The user's home folder; must be an absolute path in the platform's own style.</summary>
    public required string HomeDirectory { get; init; }

    /// <summary>Looks up an environment variable; null when it is unset.</summary>
    public Func<string, string?> GetVariable { get; init; } = static _ => null;

    /// <summary>Looks up a Windows shell folder; null when the OS has none or the shell cannot say.</summary>
    public Func<KnownFolder, string?> GetKnownFolder { get; init; } = static _ => null;

    /// <summary>Lists the root of every drive (Windows).</summary>
    public Func<IReadOnlyList<string>> ListDriveRoots { get; init; } = static () => [];

    /// <summary>Finds the folder the Steam client is installed in; null when Steam is not installed or cannot be found.</summary>
    public Func<string?> GetSteamFolder { get; init; } = static () => null;

    /// <summary>Reads a small text file (the Linux user-dirs.dirs, Steam's library list); null when it is missing or unreadable.</summary>
    public Func<string, string?> ReadTextFile { get; init; } = static _ => null;

    /// <summary>The names this machine answers to on a network (computer name, DNS name); "localhost" is implied.</summary>
    public Func<IReadOnlyList<string>> LocalHostNames { get; init; } = static () => [];

    /// <summary>Reads the machine the process runs on. Throws <see cref="InvalidOperationException"/> when the account has no home folder.</summary>
    public static PlatformEnvironment FromCurrentMachine()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetEnvironmentVariable("HOME")
                ?? throw new InvalidOperationException("This account has no home directory.");
        }

        var locator = new SteamFolderLocator(
            PlatformFactory.CurrentOs,
            home,
            OperatingSystem.IsWindows() ? WindowsSteamRegistry.ReadSteamPath : null);

        return new PlatformEnvironment
        {
            HomeDirectory = home,
            GetSteamFolder = locator.FindSteamFolder,
            GetVariable = name => Environment.GetEnvironmentVariable(name),
            GetKnownFolder = WindowsKnownFolders.Get,
            ListDriveRoots = ListDrives,
            ReadTextFile = TryReadTextFile,
            LocalHostNames = ReadLocalHostNames,
        };
    }

    // gethostname and the DNS domain are local lookups; no network traffic is needed to learn what this computer is called.
    private static IReadOnlyList<string> ReadLocalHostNames()
    {
        var names = new List<string> { Environment.MachineName };
        try
        {
            names.Add(Dns.GetHostName());
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            if (!string.IsNullOrEmpty(properties.DomainName))
            {
                names.Add($"{properties.HostName}.{properties.DomainName}");
            }
        }
        catch (Exception ex) when (ex is SocketException or NetworkInformationException)
        {
            // The computer name above is still enough to catch the common spelling.
        }

        return [.. names.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase)];
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
            // Shared access, so a program writing the file at that moment (Steam) does not make the read fail.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
