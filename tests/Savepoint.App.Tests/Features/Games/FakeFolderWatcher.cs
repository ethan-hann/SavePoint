using Savepoint.App.Platform.FolderWatching;

namespace Savepoint.App.Tests.Features.Games;

// Starts no operating-system watch: a test raises the changes itself and reads which folders were watched and which watches were stopped.
internal sealed class FakeFolderWatcher : IFolderWatcher
{
    private readonly List<FakeFolderWatch> _watches = [];

    public IReadOnlyList<FakeFolderWatch> Watches => _watches;

    // The watches nobody disposed.
    public IReadOnlyList<FakeFolderWatch> Running => [.. _watches.Where(watch => !watch.Disposed)];

    // While set, a watch is not ready until the test completes it, as when the system is still walking a big tree.
    public TaskCompletionSource? Arming { get; set; }

    // The folders the next watches fail to watch.
    public IReadOnlyList<string> Refused { get; set; } = [];

    public async Task<IFolderWatch> WatchAsync(IReadOnlyList<string> folders, CancellationToken cancellationToken = default)
    {
        var watch = new FakeFolderWatch(folders, [.. folders.Where(folder => Refused.Contains(folder))]);
        _watches.Add(watch);
        if (Arming is { } arming)
        {
            await arming.Task.WaitAsync(cancellationToken);
        }

        return watch;
    }
}

internal sealed class FakeFolderWatch(IReadOnlyList<string> folders, IReadOnlyList<string> unwatched) : IFolderWatch
{
    public IReadOnlyList<string> Folders { get; } = folders;

    // What the watch reports it could not cover; a test sets it to say a folder was lost.
    public IReadOnlyList<string> Unwatched { get; set; } = unwatched;

    public bool Disposed { get; private set; }

    // True while anyone still listens; the screen must let go of a watch it stopped.
    public bool HasListeners => Changed is not null || Faulted is not null;

    public event EventHandler? Changed;

    public event EventHandler? Faulted;

    // Delivers to whoever is still subscribed, disposed or not: an event the system had already raised reaches a listener that forgot
    // to unsubscribe, so a test of "reads nothing" fails when the screen's own guards are gone.
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void RaiseFaulted() => Faulted?.Invoke(this, EventArgs.Empty);

    public void Dispose() => Disposed = true;
}
