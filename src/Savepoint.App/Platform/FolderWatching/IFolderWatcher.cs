namespace Savepoint.App.Platform.FolderWatching;

/// <summary>
/// Tells the app when files change under a set of folders. The Games screen puts one watch on the folders of the game it shows,
/// so a change made outside SavePoint reaches the profile table without the player leaving the screen.
/// </summary>
public interface IFolderWatcher
{
    /// <summary>
    /// Starts watching <paramref name="folders"/> and everything under them. A folder that is missing or cannot be watched is skipped
    /// without an error, so the watch covers less and the caller's other ways to refresh still apply.
    /// </summary>
    /// <param name="folders">Absolute folder paths.</param>
    /// <returns>The running watch; dispose it to stop watching.</returns>
    IFolderWatch Watch(IReadOnlyList<string> folders);
}

/// <summary>A running watch on some folders. Disposing it stops all event delivery and releases the operating system's handles.</summary>
public interface IFolderWatch : IDisposable
{
    /// <summary>Raised after something under a watched folder changed. May be raised on any thread, in bursts, and never again after <see cref="IDisposable.Dispose"/>.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Raised when the watch can no longer say what changed (the operating system's buffer overflowed, or a folder's watch broke), so
    /// the caller should read everything again. The watch re-arms itself where it can. May be raised on any thread.
    /// </summary>
    event EventHandler? Faulted;
}

/// <summary>A watcher that watches nothing; the answer where no real one is supplied.</summary>
public sealed class NullFolderWatcher : IFolderWatcher
{
    /// <summary>The one instance.</summary>
    public static NullFolderWatcher Instance { get; } = new();

    /// <inheritdoc />
    public IFolderWatch Watch(IReadOnlyList<string> folders) => new NullWatch();

    private sealed class NullWatch : IFolderWatch
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public event EventHandler? Faulted
        {
            add { }
            remove { }
        }

        public void Dispose()
        {
        }
    }
}
