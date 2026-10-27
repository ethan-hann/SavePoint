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

    /// <summary>
    /// Shows <paramref name="path"/> as <see cref="Reveal"/> does, and also watches a short while for a file manager that fails right
    /// after it starts, which <see cref="Reveal"/> cannot see. The wait is off the calling thread.
    /// </summary>
    /// <param name="path">An absolute path to an existing file or folder.</param>
    /// <returns>False when nothing was started or the file manager reported a failure at once.</returns>
    Task<bool> RevealAsync(string path);
}

/// <summary>
/// Reveals paths with the Windows shell (<c>SHOpenFolderAndSelectItems</c>, falling back to Explorer's <c>/select,</c> switch),
/// Finder (<c>open -R</c>) or <c>xdg-open</c>. Every argument is passed as a list item, never joined into a shell string, and only an
/// absolute path that exists is passed at all, so a path can never be mistaken for an option.
/// </summary>
public sealed class FileManager : IFileManager
{
    // xdg-open reports a missing handler within a moment; a file manager that is still starting is left alone after this.
    private static readonly TimeSpan _openerGrace = TimeSpan.FromSeconds(3);

    private readonly OsKind _os;
    private readonly IProcessRunner _runner;
    private readonly string _explorerPath;
    private readonly IShellItemSelector _selector;

    internal FileManager(OsKind os, IProcessRunner runner, string explorerPath, IShellItemSelector selector)
    {
        _os = os;
        _runner = runner;
        _explorerPath = explorerPath;
        _selector = selector;
    }

    /// <summary>The file manager of the OS the app runs on.</summary>
    public static FileManager CreateForCurrentOs() =>
        new(
            PlatformFactory.CurrentOs,
            new SystemProcessRunner(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            new NativeShellItemSelector());

    /// <inheritdoc />
    public bool Reveal(string path)
    {
        var (ok, command) = Plan(path);
        return ok && (command is null || _runner.TryStart(command));
    }

    /// <inheritdoc />
    public async Task<bool> RevealAsync(string path)
    {
        var (ok, command) = Plan(path);
        if (!ok)
        {
            return false;
        }

        if (command is null)
        {
            return true;
        }

        // Only xdg-open's exit code means anything (3 and 4 are "no tool" and "the action failed"); Explorer exits 1 on success and open is quiet.
        return _os == OsKind.Linux ? await _runner.TryStartAndConfirmAsync(command, _openerGrace).ConfigureAwait(false) : _runner.TryStart(command);
    }

    // Ok is false when the path cannot be shown. A null command with Ok means the shell already showed it.
    private (bool Ok, ProcessCommand? Command) Plan(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return (false, null);
        }

        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (false, null);
        }

        var isFile = File.Exists(full);
        if (!isFile && !Directory.Exists(full))
        {
            return (false, null);
        }

        // Explorer's /select, switch splits on commas, so the shell is asked first, with the path as data; the switch is the fallback.
        if (_os == OsKind.Windows && isFile && TrySelectWithShell(full))
        {
            return (true, null);
        }

        return (true, _os switch
        {
            OsKind.Windows => new ProcessCommand(_explorerPath, isFile ? ["/select,", full] : [full]),
            OsKind.MacOS => new ProcessCommand("/usr/bin/open", isFile ? ["-R", full] : [full]),
            // xdg-open has no way to select a file, so a file shows up as its folder.
            _ => new ProcessCommand("xdg-open", [isFile ? Path.GetDirectoryName(full)! : full]),
        });
    }

    private bool TrySelectWithShell(string full)
    {
        try
        {
            return _selector.Select(full);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
