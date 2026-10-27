using System.Reflection;
using Savepoint.App.Hosting;
using Savepoint.App.Platform;

namespace Savepoint.App.Features.Settings;

/// <summary>Shows a file in the operating system's file manager.</summary>
public interface IFileRevealer
{
    /// <summary>Opens the file manager on the file; false when it could not be started.</summary>
    bool Reveal(string path);
}

/// <summary>What Settings needs from the machine it runs on.</summary>
/// <param name="Revealer">Shows the notices file in the file manager.</param>
/// <param name="FindNotices">Where <c>THIRD-PARTY-NOTICES.md</c> is, or null when it was not shipped; asked each time the screen shows.</param>
/// <param name="AppVersion">The version the About section states.</param>
/// <param name="LogFolder">The folder the diagnostic log is written to, or null when there is none, which hides the action that opens it.</param>
/// <param name="FindLicense">Where <c>LICENSE</c> is, or null when it was not shipped; asked each time the screen shows. A null delegate counts as not shipped.</param>
/// <param name="AppsMenu">The apps menu entry for the AppImage that is running, or null when SavePoint does not run from one, which hides the section.</param>
public sealed record SettingsEnvironment(IFileRevealer Revealer, Func<string?> FindNotices, string AppVersion, string? LogFolder = null, Func<string?>? FindLicense = null, AppsMenuEntry? AppsMenu = null)
{
    private const string _noticesFile = "THIRD-PARTY-NOTICES.md";
    private const string _licenseFile = "LICENSE";

    /// <summary>The running app: the file beside the executable and this assembly's version.</summary>
    /// <param name="revealer">Shows a file or folder in the file manager; the running system's file manager when null.</param>
    /// <param name="logFolder">The folder the diagnostic log is written to, or null when there is none.</param>
    /// <param name="clock">The clock the apps menu entry reads; the system clock when null.</param>
    public static SettingsEnvironment ForThisApp(IFileRevealer? revealer = null, string? logFolder = null, TimeProvider? clock = null) => new(revealer ?? new FileManagerRevealer(FileManager.CreateForCurrentOs()), NoticesBesideTheApp, ThisAppVersion(), logFolder, LicenseBesideTheApp, AppsMenuEntry.ForThisApp(clock));

    private static string? LicenseBesideTheApp()
    {
        var path = Path.Combine(AppContext.BaseDirectory, _licenseFile);
        return File.Exists(path) ? path : null;
    }

    private static string? NoticesBesideTheApp()
    {
        var path = Path.Combine(AppContext.BaseDirectory, _noticesFile);
        return File.Exists(path) ? path : null;
    }

    // The informational version carries build metadata after a plus sign, which a person has no use for.
    internal static string ThisAppVersion()
    {
        var assembly = typeof(SettingsEnvironment).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational?.Split('+')[0];
        return string.IsNullOrWhiteSpace(version) ? assembly.GetName().Version?.ToString(3) ?? string.Empty : version;
    }

    /// <summary>
    /// The build with everything its informational version carries, which for a package is the version and the commit it was made
    /// from ("0.9.0+3e0f9d9"); the startup log writes it so a report can be tied to a commit.
    /// </summary>
    /// <param name="assembly">The app's assembly.</param>
    internal static string BuildOf(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(informational) ? assembly.GetName().Version?.ToString(3) ?? string.Empty : informational;
    }

    internal static string ThisAppBuild() => BuildOf(typeof(SettingsEnvironment).Assembly);
}
