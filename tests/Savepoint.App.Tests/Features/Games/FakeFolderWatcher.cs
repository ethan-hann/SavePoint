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

    public event EventHandler? Changed;

    public event EventHandler? Faulted;

    // Like the real watch, nothing is delivered once it is disposed.
    public void RaiseChanged()
    {
        if (!Disposed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RaiseFaulted()
    {
        if (!Disposed)
        {
            Faulted?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => Disposed = true;
}
