using Savepoint.App.Platform.FolderWatching;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Games;

// A change made in the game's folders by something else reaches the profile table while the screen stays open: a watch on the
// folders feeds a scheduler that reads the status once the folders settle.
public sealed partial class GamesScreenViewModel
{
    private readonly StatusReadScheduler _statusReads;
    private IFolderWatch? _watch;
    private string? _watchedGameId;
    private IReadOnlyList<string> _watchedFolders = [];
    private CancellationTokenSource? _statusCts;
    private bool _shown;

    /// <inheritdoc />
    public void OnHidden()
    {
        _shown = false;
        StopWatching();
    }

    /// <summary>
    /// Tells the screen the window came to the front again. Folders that cannot be watched, and changes a watch missed, show up here, so it
    /// reads the profiles' status once more.
    /// </summary>
    public void OnWindowActivated() => _statusReads.Request();

    // Signals while a session or a job of the game runs are the game's or SavePoint's own writes. A session ends in the after-play flow,
    // which reads the status itself, and a job's end refreshes the library; reading in between would only compete with them for the disk.
    private bool StatusReadsBlocked() =>
        _disposed
        || !_shown
        || _game is not { } game
        || FlowOwns(game)
        || _deps.Jobs.List().Any(job => job.GameId == game.Id && job.Status == JobStatus.Running);

    // The profile list is cheap and the status is the scan; storage use and the folder list are left alone, since a change in a game's
    // folders moves neither.
    private async Task<bool> ReadStatusAsync()
    {
        if (_game is not { } game || IsLoading)
        {
            return false;
        }

        var generation = _generation;
        var token = (_statusCts ??= new CancellationTokenSource()).Token;
        try
        {
            var list = await _deps.Profiles.ListAsync(game.Id, token);
            var stats = await _deps.Profiles.GetRowStatsAsync(game.Id, token);

            // A reload that started meanwhile reads everything again, and a failed read leaves the rows as they were.
            if (generation == _generation && !_disposed && game.Id == _game?.Id && list.Ok && stats.Ok)
            {
                ApplyRows(list.Value, stats.Value);
                RefreshPlayAvailability();
                RaiseTableState();
            }
        }
        catch (OperationCanceledException)
        {
        }

        return true;
    }

    // Folders that are missing are left out: there is nothing to watch, and the window's activation covers them.
    private void UpdateWatch(string gameId, IReadOnlyList<FolderStatus> folders)
    {
        IReadOnlyList<string> paths = [.. folders.Where(folder => folder.Exists).Select(folder => folder.ResolvedPath ?? folder.Path).Distinct()];
        if (_watch is not null && _watchedGameId == gameId && _watchedFolders.SequenceEqual(paths))
        {
            return;
        }

        StopWatching();
        if (paths.Count == 0)
        {
            return;
        }

        var watch = _deps.Watcher.Watch(paths);
        _watch = watch;
        _watchedGameId = gameId;
        _watchedFolders = paths;
        watch.Changed += OnWatchSignalled;
        watch.Faulted += OnWatchSignalled;
    }

    // Both signals mean "read again": a fault only says the watch lost track of what changed. They come from any thread.
    private void OnWatchSignalled(object? sender, EventArgs e) =>
        _deps.Dispatcher.Post(() =>
        {
            if (ReferenceEquals(sender, _watch))
            {
                _statusReads.Request();
            }
        });

    private void StopWatching()
    {
        _statusReads.Cancel();
        _statusCts?.Cancel();
        _statusCts = null;
        if (_watch is { } watch)
        {
            watch.Changed -= OnWatchSignalled;
            watch.Faulted -= OnWatchSignalled;
            watch.Dispose();
        }

        _watch = null;
        _watchedGameId = null;
        _watchedFolders = [];
    }
}
