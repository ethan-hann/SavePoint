using Savepoint.Core.Platform;

namespace Savepoint.Core.Artwork;

/// <summary>Finds the folder GOG Galaxy keeps the images it downloaded in, so artwork Galaxy already has needs no network (D30).</summary>
public interface IGogGalaxyCacheLocator
{
    /// <summary>Looks for Galaxy's <c>webcache</c> folder on this computer.</summary>
    /// <returns>The folder, or null when Galaxy is not installed or cannot be found. Never throws.</returns>
    string? FindWebcacheFolder();
}

/// <summary>
/// Finds Galaxy's <c>webcache</c> folder under <c>ProgramData\GOG.com\Galaxy</c> on Windows. Galaxy for macOS keeps it somewhere this
/// has not been checked against a real installation, so other systems find nothing. Only a folder that exists is returned.
/// </summary>
public sealed class GogGalaxyCacheLocator : IGogGalaxyCacheLocator
{
    private readonly OsKind _os;
    private readonly string? _commonAppData;
    private readonly Func<string, bool> _directoryExists;

    /// <summary>Creates a locator for one operating system.</summary>
    /// <param name="os">Which operating system's places to look in.</param>
    /// <param name="commonAppData">The machine-wide application data folder (<c>C:\ProgramData</c>); null when the system has none.</param>
    /// <param name="directoryExists">Tells whether a folder exists; the real file system when null.</param>
    public GogGalaxyCacheLocator(OsKind os, string? commonAppData, Func<string, bool>? directoryExists = null)
    {
        _os = os;
        _commonAppData = commonAppData;
        _directoryExists = directoryExists ?? Directory.Exists;
    }

    /// <summary>Builds the locator for the platform the app runs on.</summary>
    /// <param name="platform">The platform whose operating system decides where to look.</param>
    public static GogGalaxyCacheLocator ForPlatform(IPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return new GogGalaxyCacheLocator(platform.Os, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
    }

    /// <inheritdoc />
    public string? FindWebcacheFolder()
    {
        if (_os != OsKind.Windows || string.IsNullOrWhiteSpace(_commonAppData))
        {
            return null;
        }

        try
        {
            var folder = Path.Combine(_commonAppData, "GOG.com", "Galaxy", "webcache");
            return _directoryExists(folder) ? folder : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
