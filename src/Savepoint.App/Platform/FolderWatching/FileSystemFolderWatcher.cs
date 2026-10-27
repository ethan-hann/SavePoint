using Savepoint.Core.Diagnostics;

namespace Savepoint.App.Platform.FolderWatching;

/// <summary>
/// Watches folders with the operating system's own change notifications (<see cref="FileSystemWatcher"/>: ReadDirectoryChangesW,
/// inotify, FSEvents), which cost nothing while nothing changes, so a 90 GB install is no dearer to watch than an empty one.
/// </summary>
public sealed class FileSystemFolderWatcher : IFolderWatcher
{
    private readonly IDiagnosticLog _log;
    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <param name="log">Where a folder that cannot be watched is noted, once for each folder.</param>
    public FileSystemFolderWatcher(IDiagnosticLog log)
    {
        _log = log;
    }

    /// <inheritdoc />
    public IFolderWatch Watch(IReadOnlyList<string> folders) => new FileSystemFolderWatch(folders, NoteUnwatched);

    // A game's folders are watched again each time the player comes back to it, so the same folder would otherwise fill the log.
    private void NoteUnwatched(string folder, Exception? error)
    {
        lock (_gate)
        {
            if (!_reported.Add(folder))
            {
                return;
            }
        }

        _log.Warning($"Cannot watch '{folder}' for changes; the Games screen reads its status when the window is activated instead.", error);
    }
}

/// <summary>
/// The watch <see cref="FileSystemFolderWatcher"/> starts: one operating-system watcher for each folder that exists and can be
/// watched, all feeding one signal.
/// </summary>
internal sealed class FileSystemFolderWatch : IFolderWatch
{
    // A program writing thousands of files raises thousands of events; the listener only needs to hear that something changed.
    internal static readonly TimeSpan SignalGap = TimeSpan.FromMilliseconds(100);

    // The default buffer (8 KB) holds a few dozen events and overflows under a mod install.
    private const int _bufferBytes = 64 * 1024;

    private readonly Action<string, Exception?> _noteUnwatched;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ITimer _gapTimer;
    private int _signalPending;
    private bool _disposed;

    public FileSystemFolderWatch(IReadOnlyList<string> folders, Action<string, Exception?> noteUnwatched)
    {
        _noteUnwatched = noteUnwatched;
        _gapTimer = TimeProvider.System.CreateTimer(_ => RaiseChanged(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Start(folder);
        }
    }

    public event EventHandler? Changed;

    public event EventHandler? Faulted;

    public void Dispose()
    {
        FileSystemWatcher[] stopping;
        lock (_gate)
        {
            _disposed = true;
            stopping = [.. _watchers.Values];
            _watchers.Clear();
        }

        foreach (var watcher in stopping)
        {
            Release(watcher);
        }

        _gapTimer.Dispose();
    }

    // The watcher stays down for a folder that can't be watched (missing, no right to read it, or the system out of watches on a huge
    // tree); the caller's other ways to refresh cover it.
    private void Start(string folder)
    {
        if (!Directory.Exists(folder))
        {
            _noteUnwatched(folder, null);
            return;
        }

        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = _bufferBytes,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            };
            watcher.Created += OnEvent;
            watcher.Changed += OnEvent;
            watcher.Deleted += OnEvent;
            watcher.Renamed += OnEvent;
            watcher.Error += (_, _) => HandleError(folder);
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException or InvalidOperationException or NotSupportedException)
        {
            if (watcher is not null)
            {
                Release(watcher);
            }

            _noteUnwatched(folder, e);
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                Release(watcher);
                return;
            }

            _watchers[folder] = watcher;
        }
    }

    // The operating system dropped events (its buffer overflowed) or the watch broke, so nothing says what changed: tell the listener to
    // read everything, and start the folder's watch again, which is also how a watch that stopped on its own comes back.
    internal void HandleError(string folder)
    {
        FileSystemWatcher? old;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _watchers.Remove(folder, out old);
        }

        if (old is not null)
        {
            Release(old);
        }

        Start(folder);
        Faulted?.Invoke(this, EventArgs.Empty);
    }

    private void OnEvent(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _signalPending, 1) == 0)
        {
            try
            {
                _gapTimer.Change(SignalGap, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void RaiseChanged()
    {
        Volatile.Write(ref _signalPending, 0);
        if (!Volatile.Read(ref _disposed))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static void Release(FileSystemWatcher watcher)
    {
        try
        {
            watcher.EnableRaisingEvents = false;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // Already stopped by the system; disposing is all that is left.
        }

        watcher.Dispose();
    }
}
