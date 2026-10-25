using System.Text;
using Avalonia.Platform;
using Savepoint.App.Platform.Processes;

namespace Savepoint.App.Platform;

/// <summary>Where SavePoint's apps menu entry stands.</summary>
public enum AppsMenuState
{
    /// <summary>No entry exists.</summary>
    NotAdded,

    /// <summary>SavePoint's entry exists and opens this file.</summary>
    Added,

    /// <summary>SavePoint's entry exists but opens another file, or one of its icons is missing.</summary>
    OutOfDate,

    /// <summary>A file SavePoint did not write is in the way; SavePoint leaves it alone.</summary>
    Foreign,
}

/// <summary>What an add or a remove ended with.</summary>
public enum AppsMenuOutcome
{
    /// <summary>It worked.</summary>
    Done,

    /// <summary>A file SavePoint did not write is in the way, so nothing was changed.</summary>
    Foreign,

    /// <summary>A folder could not be written; anything written part way is undone when the entry was new.</summary>
    Failed,
}

/// <summary>
/// Adds SavePoint to the desktop's apps menu from an AppImage, and takes it out again. It writes one desktop entry and the icon
/// at each size into the user's own data folder, marks the entry as its own, and never touches a file that does not carry the
/// mark. Nothing here needs elevated rights.
/// </summary>
public sealed class AppsMenuEntry
{
    private const string _entryFile = "savepoint.desktop";
    private const string _iconName = "savepoint";
    private const string _marker = "X-SavePoint-Managed=true";
    private const long _largestEntryBytes = 64 * 1024;
    private static readonly int[] _iconSizes = [256, 512];
    private static readonly TimeSpan _cacheToolTimeout = TimeSpan.FromSeconds(10);

    private readonly string _appImagePath;
    private readonly string _applicationsFolder;
    private readonly string _hicolorFolder;
    private readonly Func<int, Stream?> _openIcon;
    private readonly IProcessRunner _runner;

    internal AppsMenuEntry(string appImagePath, string dataHome, Func<int, Stream?> openIcon, IProcessRunner runner)
    {
        _appImagePath = appImagePath;
        _applicationsFolder = Path.Combine(dataHome, "applications");
        _hicolorFolder = Path.Combine(dataHome, "icons", "hicolor");
        _openIcon = openIcon;
        _runner = runner;
    }

    private string EntryPath => Path.Combine(_applicationsFolder, _entryFile);

    /// <summary>
    /// The entry for the running app: null unless this is Linux, the <c>APPIMAGE</c> variable names the AppImage file, and this
    /// process runs from the folder <c>APPDIR</c> names (the variables are inherited, so they can describe another program).
    /// </summary>
    public static AppsMenuEntry? ForThisApp() =>
        For(
            OperatingSystem.IsLinux(),
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            OpenBundledIcon,
            new SystemProcessRunner(),
            Environment.ProcessPath);

    /// <summary>Where the user's data files go: <c>XDG_DATA_HOME</c> when it is an absolute path, else <c>.local/share</c> in the home folder.</summary>
    internal static string ResolveDataHome(Func<string, string?> environment, string homeFolder)
    {
        var configured = environment("XDG_DATA_HOME");
        return !string.IsNullOrEmpty(configured) && Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(homeFolder, ".local", "share");
    }

    internal static AppsMenuEntry? For(bool isLinux, Func<string, string?> environment, string homeFolder, Func<int, Stream?> openIcon, IProcessRunner runner, string? processPath)
    {
        var appImage = environment("APPIMAGE");
        // A line break cannot be written into a desktop entry, so such a path gets no action rather than a broken entry.
        if (!isLinux || string.IsNullOrEmpty(appImage) || !Path.IsPathRooted(appImage) || appImage.Any(char.IsControl))
        {
            return null;
        }

        // The variable is inherited by child processes, so it only counts when this very process runs from the AppImage's mount.
        if (!RunsFromMount(environment("APPDIR"), processPath))
        {
            return null;
        }

        return new AppsMenuEntry(appImage, ResolveDataHome(environment, homeFolder), openIcon, runner);
    }

    private static bool RunsFromMount(string? appDir, string? processPath)
    {
        if (string.IsNullOrEmpty(appDir) || string.IsNullOrEmpty(processPath) || !Path.IsPathRooted(appDir) || !Path.IsPathRooted(processPath))
        {
            return false;
        }

        // The trailing separator keeps a sibling folder with the same prefix from passing.
        var mount = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDir)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(processPath).StartsWith(mount, StringComparison.Ordinal);
    }

    /// <summary>Says where the entry stands, from the files on disk.</summary>
    public AppsMenuState GetState()
    {
        // Icons left behind without an entry count as not added: an add overwrites them and a remove cleans them.
        if (Directory.Exists(EntryPath))
        {
            return AppsMenuState.Foreign;
        }

        var info = new FileInfo(EntryPath);
        if (!info.Exists)
        {
            return AppsMenuState.NotAdded;
        }

        // A real entry is a small regular file; anything else is not ours and is never read.
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > _largestEntryBytes)
        {
            return AppsMenuState.Foreign;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(EntryPath);
        }
        catch (IOException)
        {
            return AppsMenuState.Foreign;
        }
        catch (UnauthorizedAccessException)
        {
            return AppsMenuState.Foreign;
        }

        if (!lines.Contains(_marker))
        {
            return AppsMenuState.Foreign;
        }

        var current = lines.Contains(ExecLine(_appImagePath)) && lines.Contains(TryExecLine(_appImagePath)) && _iconSizes.All(size => File.Exists(IconPath(size)));
        return current ? AppsMenuState.Added : AppsMenuState.OutOfDate;
    }

    /// <summary>Adds the entry, or points an out-of-date one at this file. A file SavePoint did not write is never replaced.</summary>
    public async Task<AppsMenuOutcome> AddAsync(string comment)
    {
        var before = GetState();
        if (before == AppsMenuState.Foreign)
        {
            return AppsMenuOutcome.Foreign;
        }

        try
        {
            // Read first, so an icon that cannot be read stops the add before anything is written.
            var icons = ReadIcons();
            try
            {
                // The entry goes last, so a failure part way never replaces an entry that was working.
                foreach (var (size, bytes) in icons)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(IconPath(size))!);
                    WriteAtomically(IconPath(size), bytes);
                }

                Directory.CreateDirectory(_applicationsFolder);
                WriteAtomically(EntryPath, Encoding.UTF8.GetBytes(Render(_appImagePath, comment)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (before == AppsMenuState.NotAdded)
                {
                    DeleteWhatWasWritten(carryOn: true);
                }

                throw;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AppsMenuOutcome.Failed;
        }

        await RefreshCachesAsync().ConfigureAwait(false);
        return AppsMenuOutcome.Done;
    }

    /// <summary>Removes the entry and the icons the add wrote; a file without SavePoint's mark is left alone.</summary>
    public async Task<AppsMenuOutcome> RemoveAsync()
    {
        switch (GetState())
        {
            case AppsMenuState.Foreign:
                return AppsMenuOutcome.Foreign;
            case AppsMenuState.NotAdded when !_iconSizes.Any(size => File.Exists(IconPath(size))):
                return AppsMenuOutcome.Done;
        }

        try
        {
            DeleteWhatWasWritten();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AppsMenuOutcome.Failed;
        }

        await RefreshCachesAsync().ConfigureAwait(false);
        return AppsMenuOutcome.Done;
    }

    /// <summary>The text of the desktop entry for an AppImage at the given path.</summary>
    internal static string Render(string appImagePath, string comment) =>
        string.Join(
            '\n',
            "[Desktop Entry]",
            "Type=Application",
            "Name=SavePoint",
            $"Comment={comment}",
            ExecLine(appImagePath),
            TryExecLine(appImagePath),
            $"Icon={_iconName}",
            "Terminal=false",
            "Categories=Utility;",
            $"StartupWMClass={DesktopIdentity.WindowClass}",
            _marker) + "\n";

    // TryExec is a plain path, not a command line, so only the string-level backslash escape applies. A desktop hides the entry
    // when this file is gone.
    private static string TryExecLine(string path) => "TryExec=" + path.Replace(@"\", @"\\", StringComparison.Ordinal);

    // The path is quoted and its special characters escaped as the desktop entry rules ask, then each backslash is doubled
    // because the file format reads the value as a string first. A percent sign would otherwise start a field code.
    private static string ExecLine(string path)
    {
        var quoted = new StringBuilder("Exec=\"");
        foreach (var c in path)
        {
            switch (c)
            {
                case '\\':
                    quoted.Append(@"\\\\");
                    break;
                case '"' or '`' or '$':
                    quoted.Append(@"\\").Append(c);
                    break;
                case '%':
                    quoted.Append("%%");
                    break;
                default:
                    quoted.Append(c);
                    break;
            }
        }

        return quoted.Append('"').ToString();
    }

    private string IconPath(int size) => Path.Combine(_hicolorFolder, $"{size}x{size}", "apps", _iconName + ".png");

    private List<(int Size, byte[] Bytes)> ReadIcons()
    {
        var icons = new List<(int, byte[])>();
        foreach (var size in _iconSizes)
        {
            using var stream = _openIcon(size);
            // An entry whose icon cannot be installed would never read as current, so the add fails instead.
            if (stream is null)
            {
                throw new FileNotFoundException($"The app carries no {size} px icon.");
            }

            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            icons.Add((size, copy.ToArray()));
        }

        return icons;
    }

    // An undo after a failed add carries on past a file it cannot delete; a remove stops and reports it.
    private void DeleteWhatWasWritten(bool carryOn = false)
    {
        foreach (var path in _iconSizes.Select(IconPath).Append(EntryPath))
        {
            try
            {
                File.Delete(path);
            }
            catch (DirectoryNotFoundException)
            {
                // Its folder was never made, so there is nothing to delete.
            }
            catch (Exception ex) when (carryOn && ex is IOException or UnauthorizedAccessException)
            {
                // The next file may still go.
            }
        }
    }

    // A reader never sees half a file: the bytes go to a neighbor with a name nobody else can have picked, and the neighbor takes
    // the real name. A neighbor that was written but not moved is deleted again.
    private static void WriteAtomically(string path, byte[] bytes)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                stream.Write(bytes);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The original failure is the one to report.
            }

            throw;
        }
    }

    // Both tools are optional helpers and only ever see the user's own folders; a missing tool or a failed run changes nothing.
    // The icon cache tool runs only when a cache file already exists, since it would otherwise create one that then goes stale
    // against the system theme; without one, touching the folder is what tells the desktop to look again.
    private async Task RefreshCachesAsync()
    {
        await RunQuietlyAsync(new ProcessCommand("update-desktop-database", [_applicationsFolder])).ConfigureAwait(false);
        if (File.Exists(Path.Combine(_hicolorFolder, "icon-theme.cache")))
        {
            await RunQuietlyAsync(new ProcessCommand("gtk-update-icon-cache", ["-q", "-t", "-f", _hicolorFolder])).ConfigureAwait(false);
        }
        else if (Directory.Exists(_hicolorFolder))
        {
            try
            {
                Directory.SetLastWriteTimeUtc(_hicolorFolder, DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The desktop notices the change on its own, only later.
            }
        }
    }

    private async Task RunQuietlyAsync(ProcessCommand command)
    {
        try
        {
            await _runner.RunAsync(command, _cacheToolTimeout).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Nothing to do: the entry works without a refreshed cache, only later.
        }
    }

    internal static Stream? OpenBundledIcon(int size)
    {
        var uri = new Uri($"avares://SavePoint/Assets/Icon/savepoint-{size}.png");
        return AssetLoader.Exists(uri) ? AssetLoader.Open(uri) : null;
    }
}
