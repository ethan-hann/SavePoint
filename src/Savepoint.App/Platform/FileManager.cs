using Savepoint.App.Platform.Processes;
using Savepoint.Core.Platform;

namespace Savepoint.App.Platform;

/// <summary>Shows a file or folder in the operating system's file manager.</summary>
public interface IFileManager
{
    /// <summary>
    /// Shows <paramref name="path"/> in the file manager: a file is selected in its folder where the file manager can
    /// select, otherwise its folder is opened; a folder is opened.
    /// </summary>
    /// <param name="path">An absolute path to an existing file or folder.</param>
    /// <returns>False when nothing was started: the path is empty, relative or missing, or the file manager could not be started.</returns>
    bool Reveal(string path);
}

/// <summary>
/// Reveals paths with Explorer (<c>/select,</c>), Finder (<c>open -R</c>) or <c>xdg-open</c>. Every argument is passed as a
/// list item, never joined into a shell string, and only an absolute path that exists is passed at all, so a path can
/// never be mistaken for an option.
/// </summary>
public sealed class FileManager : IFileManager
{
    private readonly OsKind _os;
    private readonly IProcessRunner _runner;
    private readonly string _explorerPath;

    internal FileManager(OsKind os, IProcessRunner runner, string explorerPath)
    {
        _os = os;
        _runner = runner;
        _explorerPath = explorerPath;
    }

    /// <summary>The file manager of the OS the app runs on.</summary>
    public static FileManager CreateForCurrentOs() =>
        new(
            PlatformFactory.CurrentOs,
            new SystemProcessRunner(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));

    /// <inheritdoc />
    public bool Reveal(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var isFile = File.Exists(full);
        if (!isFile && !Directory.Exists(full))
        {
            return false;
        }

        return _runner.TryStart(_os switch
        {
            OsKind.Windows => new ProcessCommand(_explorerPath, isFile ? ["/select,", full] : [full]),
            OsKind.MacOS => new ProcessCommand("/usr/bin/open", isFile ? ["-R", full] : [full]),
            // xdg-open has no way to select a file, so a file shows up as its folder.
            _ => new ProcessCommand("xdg-open", [isFile ? Path.GetDirectoryName(full)! : full]),
        });
    }
}
