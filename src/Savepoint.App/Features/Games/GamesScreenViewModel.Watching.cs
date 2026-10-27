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
    private bool _watchFaulted;

    /// <inheritdoc />
    public void OnHidden()
    {
        _shown = false;
        StopWatching();
    }

    // Nothing is read while the window is behind another or minimized: a game started elsewhere writes all the time, and a scan of its
    // install would compete with it for the disk. The window coming back reads once, which also covers folders that cannot be watched
    // and changes a watch missed.
    private void OnWindowActivityChanged(object? sender, EventArgs e)
    {
        if (IsWindowInFront())
        {
            _statusReads.Request();
        }
    }

    private bool IsWindowInFront() => _deps.Window.Current is { IsActive: true, IsMinimized: false };

    // Signals while a session or a job of the game runs are the game's or SavePoint's own writes. A session ends in the after-play flow,
    // which reads the status itself, and a job's end refreshes the library; reading in between would only compete with them for the disk.
    private bool StatusReadsBlocked() =>
        _disposed
        || !_shown
        || !IsWindowInFront()
        || _game is not { } game
        || FlowOwns(game)
        || _deps.Jobs.List().Any(job => job.GameId == game.Id && job.Status == JobStatus.Running);

    // The profile list is cheap and the status is the scan; storage use is left alone, since a change in a game's folders does not move
    // it. The folders are looked at first (a cheap check of which exist), so a folder that appeared, or a watch that lost one after a
    // fault, is watched again before the scan reads anything.
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
            var folders = await _deps.Games.GetFolderStatusAsync(game.Id, token);
            if (folders.Ok && generation == _generation && !_disposed && _shown && game.Id == _game?.Id)
            {
                var retryFailed = _watchFaulted;
                _watchFaulted = false;
                await UpdateWatchAsync(game.Id, folders.Value, retryFailed, generation, token);
            }

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

    // Folders that are missing are left out: there is nothing to watch, and a later look at the folders finds them when they appear. A
    // watch that could not cover a folder is set up again only when asked to retry (a reload, or a read after a fault), so a folder the
    // system refuses is not tried at every read. The watch is ready before this returns, so a status scan that follows cannot miss a
    // change made during it.
    private async Task UpdateWatchAsync(string gameId, IReadOnlyList<FolderStatus> folders, bool retryFailed, long generation, CancellationToken token)
    {
        IReadOnlyList<string> paths = [.. folders.Where(folder => folder.Exists).Select(folder => folder.ResolvedPath ?? folder.Path).Distinct()];
        if (_watch is { } current && _watchedGameId == gameId && _watchedFolders.SequenceEqual(paths) && !(retryFailed && current.Unwatched.Count > 0))
        {
            return;
        }

        ReleaseWatch();
        if (paths.Count == 0)
        {
            return;
        }

        IFolderWatch watch;
        try
        {
            watch = await _deps.Watcher.WatchAsync(paths, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // The screen moved on while the system set the watch up: a newer reload brings its own.
        if (generation != _generation || _disposed || !_shown || _watch is not null)
        {
            watch.Dispose();
            return;
        }

        _watch = watch;
        _watchedGameId = gameId;
        _watchedFolders = paths;
        watch.Changed += OnWatchSignalled;
        watch.Faulted += OnWatchFaulted;
    }

    // They come from any thread.
    private void OnWatchSignalled(object? sender, EventArgs e) =>
        _deps.Dispatcher.Post(() =>
        {
            if (ReferenceEquals(sender, _watch))
            {
                _statusReads.Request();
            }
        });

    // The watch lost track of what changed, and may have lost a folder: read again, and look at the folders first.
    private void OnWatchFaulted(object? sender, EventArgs e) =>
        _deps.Dispatcher.Post(() =>
        {
            if (ReferenceEquals(sender, _watch))
            {
                _watchFaulted = true;
                _statusReads.Request();
            }
        });

    private void StopWatching()
    {
        _statusReads.Cancel();
        _statusCts?.Cancel();
        _statusCts = null;
        ReleaseWatch();
    }

    private void ReleaseWatch()
    {
        if (_watch is { } watch)
        {
            watch.Changed -= OnWatchSignalled;
            watch.Faulted -= OnWatchFaulted;
            watch.Dispose();
        }

        _watch = null;
        _watchedGameId = null;
        _watchedFolders = [];
        _watchFaulted = false;
    }
}
