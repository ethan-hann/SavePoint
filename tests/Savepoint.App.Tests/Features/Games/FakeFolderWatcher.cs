using Savepoint.App.Platform.FolderWatching;

namespace Savepoint.App.Tests.Features.Games;

// Starts no operating-system watch: a test raises the changes itself and reads which folders were watched and which watches were stopped.
internal sealed class FakeFolderWatcher : IFolderWatcher
{
    private readonly List<FakeFolderWatch> _watches = [];

    public IReadOnlyList<FakeFolderWatch> Watches => _watches;

    // The watches nobody disposed.
    public IReadOnlyList<FakeFolderWatch> Running => [.. _watches.Where(watch => !watch.Disposed)];

    public IFolderWatch Watch(IReadOnlyList<string> folders)
    {
        var watch = new FakeFolderWatch(folders);
        _watches.Add(watch);
        return watch;
    }
}

internal sealed class FakeFolderWatch(IReadOnlyList<string> folders) : IFolderWatch
{
    public IReadOnlyList<string> Folders { get; } = folders;

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
