using System.Diagnostics;
using System.Reflection;

namespace Savepoint.App.Features.Settings;

/// <summary>Shows a file in the operating system's file manager.</summary>
public interface IFileRevealer
{
    /// <summary>Opens the file manager on the file; false when it could not be started.</summary>
    bool Reveal(string path);
}

/// <summary>The real revealer: Explorer with the file selected, Finder's reveal, or the folder through xdg-open.</summary>
public sealed class ProcessFileRevealer : IFileRevealer
{
    /// <inheritdoc />
    public bool Reveal(string path)
    {
        try
        {
            using var process = Process.Start(Start(path));
            return process is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return false;
        }
    }

    /// <summary>The process that reveals a file on the running system.</summary>
    public static ProcessStartInfo Start(string path) => Start(path, OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux");

    /// <summary>The process that reveals a file on the named system (windows, macos or linux).</summary>
    public static ProcessStartInfo Start(string path, string os)
    {
        switch (os)
        {
            case "windows":
                // Explorer takes its one argument as a single string, quoting included.
                return new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{path}\"", UseShellExecute = false };
            case "macos":
                return WithArguments(new ProcessStartInfo("open"), "-R", path);
            default:
                return WithArguments(new ProcessStartInfo("xdg-open"), FolderOf(path));
        }
    }

    // Cut at the last separator of either kind, so the answer does not depend on the system the code runs on.
    private static string FolderOf(string path)
    {
        var cut = path.LastIndexOfAny(['/', '\\']);
        return cut > 0 ? path[..cut] : path;
    }

    private static ProcessStartInfo WithArguments(ProcessStartInfo start, params string[] arguments)
    {
        start.UseShellExecute = false;
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }
}

/// <summary>What Settings needs from the machine it runs on.</summary>
/// <param name="Revealer">Shows the notices file in the file manager.</param>
/// <param name="FindNotices">Where <c>THIRD-PARTY-NOTICES.md</c> is, or null when it was not shipped; asked each time the screen shows.</param>
/// <param name="AppVersion">The version the About section states.</param>
public sealed record SettingsEnvironment(IFileRevealer Revealer, Func<string?> FindNotices, string AppVersion)
{
    private const string _noticesFile = "THIRD-PARTY-NOTICES.md";

    /// <summary>The running app: the file beside the executable and this assembly's version.</summary>
    public static SettingsEnvironment ForThisApp() => new(new ProcessFileRevealer(), NoticesBesideTheApp, ThisAppVersion());

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
}
