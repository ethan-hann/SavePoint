using System.Security;
using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Windows;

namespace Savepoint.Core.Artwork;

/// <summary>Finds the folder the Steam client is installed in, which holds the artwork Steam already downloaded (D30).</summary>
public interface ISteamFolderLocator
{
    /// <summary>Looks for the Steam client's folder on this computer.</summary>
    /// <returns>The folder, or null when Steam is not installed or cannot be found. Never throws.</returns>
    string? FindSteamFolder();
}

/// <summary>
/// Finds Steam where each operating system puts it: the <c>SteamPath</c> registry value on Windows, and the usual folders in the
/// home folder on Linux (including the Flatpak one) and macOS. Only folders that exist are returned.
/// </summary>
public sealed class SteamFolderLocator : ISteamFolderLocator
{
    private readonly OsKind _os;
    private readonly string _home;
    private readonly Func<string?>? _readWindowsSteamPath;
    private readonly Func<string, bool> _directoryExists;

    /// <summary>Creates a locator for one operating system over a home folder.</summary>
    /// <param name="os">Which operating system's places to look in.</param>
    /// <param name="homeDirectory">The user's home folder in that operating system's path style.</param>
    /// <param name="readWindowsSteamPath">Reads the <c>SteamPath</c> registry value; null on other systems, and returns null when the value is missing.</param>
    /// <param name="directoryExists">Tells whether a folder exists; the real file system when null.</param>
    public SteamFolderLocator(OsKind os, string homeDirectory, Func<string?>? readWindowsSteamPath = null, Func<string, bool>? directoryExists = null)
    {
        ArgumentNullException.ThrowIfNull(homeDirectory);
        _os = os;
        _home = homeDirectory;
        _readWindowsSteamPath = readWindowsSteamPath;
        _directoryExists = directoryExists ?? Directory.Exists;
    }

    /// <summary>Builds the locator for the platform the app runs on, reading the real registry on Windows.</summary>
    /// <param name="platform">The platform whose operating system and home folder decide where to look.</param>
    public static SteamFolderLocator ForPlatform(IPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return new SteamFolderLocator(platform.Os, platform.Directories.HomeDir, OperatingSystem.IsWindows() ? WindowsSteamRegistry.ReadSteamPath : null);
    }

    /// <inheritdoc />
    public string? FindSteamFolder()
    {
        try
        {
            return _os switch
            {
                OsKind.Windows => FromRegistry(),
                OsKind.MacOS => FirstExisting(Path.Combine(_home, "Library", "Application Support", "Steam")),
                _ => FirstExisting(
                    Path.Combine(_home, ".steam", "steam"),
                    Path.Combine(_home, ".local", "share", "Steam"),
                    Path.Combine(_home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
                    Path.Combine(_home, "snap", "steam", "common", ".local", "share", "Steam")),
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    // The registry keeps the path with forward slashes and whatever case the installer used.
    private string? FromRegistry()
    {
        if (_readWindowsSteamPath?.Invoke() is not { Length: > 0 } stored)
        {
            return null;
        }

        var path = stored.Replace('/', '\\');
        return _directoryExists(path) ? path : null;
    }

    private string? FirstExisting(params string[] candidates) =>
        candidates.FirstOrDefault(_directoryExists);
}
