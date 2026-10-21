using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Services;
using Savepoint.Core.Services.Jobs;

namespace Savepoint.Core.Launch.Orchestration;

public sealed partial class LaunchOrchestrator
{
    // What the job learned while it held the game's lock, for the part that runs after the lock is let go.
    private sealed record Prepared(GameDefinition Game, string InstallPath, ResolvedTarget Resolved);

    /// <inheritdoc />
    public Task<OperationResult<JobStarted>> StartAsync(string gameId, LaunchTarget target) =>
        ApiGuard.RunAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(target);
            var game = await RequireGameAsync(gameId).ConfigureAwait(false);

            // The claim makes the check and the session one step: a second call made at the same moment finds the game taken.
            if (!_book.TryClaim(gameId))
            {
                throw new LaunchAlreadyRunningException(gameId);
            }

            var opened = false;
            try
            {
                if (await IsGameOpenAsync(game).ConfigureAwait(false))
                {
                    throw new LaunchAlreadyRunningException(gameId);
                }

                var (profileId, older) = await ResolveForSessionAsync(game, target).ConfigureAwait(false);

                // The work waits until the session exists, so its first change always finds it.
                var sessionOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var jobId = _services.Jobs.Start(JobKind.Launch, gameId, cancellable: true, context => RunLaunchAsync(context, gameId, target, sessionOpen.Task));
                _book.Open(gameId, profileId, target, older, jobId);
                opened = true;
                sessionOpen.SetResult();
                return new JobStarted(jobId);
            }
            finally
            {
                if (!opened)
                {
                    _book.Unclaim(gameId);
                }
            }
        });

    // A game is open when the session is, or when the game's own process is still alive: a player who said they were done may have
    // left it running, and a game started outside SavePoint shows up by the process name its definition gives.
    private async Task<bool> IsGameOpenAsync(GameDefinition game)
    {
        if (_book.Record(game.Id)?.Watcher is { } previous && previous.IsRunning())
        {
            return true;
        }

        return await Task.Run(() => _starter.IsRunning(game.Launch)).ConfigureAwait(false);
    }

    // The session names its profile and whether it starts from an older point before the job has switched anything, so both are read now.
    private async Task<(string ProfileId, bool Older)> ResolveForSessionAsync(GameDefinition game, LaunchTarget target)
    {
        var profiles = await _services.Catalog.ListProfilesAsync(game.Id).ConfigureAwait(false);
        return await _services.Storage.ReadAsync(async storage =>
        {
            var repos = GameFolders.Repos(_services, storage, game);
            var resolved = await TargetResolver.ResolveAsync(storage, repos, profiles, target, CancellationToken.None).ConfigureAwait(false);
            return (resolved.Profile.Id, resolved.IsOlderPoint);
        }).ConfigureAwait(false);
    }

    private async Task<JobResult?> RunLaunchAsync(JobContext context, string gameId, LaunchTarget target, Task sessionOpen)
    {
        await sessionOpen.ConfigureAwait(false);

        // Preparing and switching hold the game's lock, which a running optimization yields to (D13.17); the lock is let go before the game starts.
        var prepared = await _services.RunForGameAsync(gameId, (storage, _) => PrepareAsync(storage, context, gameId, target), context.CancellationToken).ConfigureAwait(false);
        context.CancellationToken.ThrowIfCancellationRequested();

        var watcher = await _starter.StartAsync(prepared.Game.Launch, prepared.InstallPath, context.CancellationToken).ConfigureAwait(false);
        await RecordLaunchAsync(prepared.Resolved.Profile.Id, watcher.StartedAt).ConfigureAwait(false);
        var running = _book.TryChange(gameId, context.JobId, record =>
        {
            record.Watcher = watcher;
            return new SessionState.Running(watcher.StartedAt);
        });
        if (!running)
        {
            watcher.Dispose();
            return null;
        }

        _ = FollowGameAsync(gameId, context.JobId, watcher);
        return null;
    }

    // Both folders are read before either is touched (D13.8): one with unsaved work stops the launch with nothing switched.
    private async Task<Prepared> PrepareAsync(StorageSnapshot storage, JobContext context, string gameId, LaunchTarget target)
    {
        var token = context.CancellationToken;
        var game = await RequireGameAsync(gameId, token).ConfigureAwait(false);
        var profiles = await _services.Catalog.ListProfilesAsync(gameId, token).ConfigureAwait(false);
        var repos = GameFolders.Repos(_services, storage, game);
        var resolved = await TargetResolver.ResolveAsync(storage, repos, profiles, target, token).ConfigureAwait(false);
        var readings = await Task.WhenAll(resolved.Folders.Select(folder => FolderReading.ReadAsync(storage.Engine, folder, profiles, token))).ConfigureAwait(false);

        // A half-switched folder is refused with its own error unless it is the very switch this launch would finish.
        if (readings.FirstOrDefault(reading => reading.Dirty) is { } blocked)
        {
            throw blocked.Blocked
                ? new InterruptedSwitchException(blocked.Target.Role, blocked.Note!.ToCommit)
                : new DirtyWorkingTreeException(blocked.Target.Role);
        }

        var pending = readings.Where(reading => reading.NeedsSwitch).ToList();
        if (pending.Count > 0)
        {
            await SwitchAsync(storage, context, gameId, pending).ConfigureAwait(false);
        }

        _book.TryUpdate(gameId, context.JobId, record => record.StartCommits = resolved.Folders.ToDictionary(folder => folder.Role, folder => folder.Commit));
        return new Prepared(game, repos.Single(repo => repo.Role == RepoRole.Install).WorkTree, resolved);
    }

    private async Task SwitchAsync(StorageSnapshot storage, JobContext context, string gameId, IReadOnlyList<FolderReading> folders)
    {
        Report(context, gameId, JobProgress.None);
        var done = 0;
        foreach (var folder in folders)
        {
            var offset = done;
            var last = 0;
            var progress = new InlineProgress<FileProgress>(value =>
            {
                last = value.FilesDone;
                Report(context, gameId, new JobProgress(offset + value.FilesDone, value.FilesTotal is { } total ? offset + total : null, null, null, value.CurrentPath));
            });
            var target = folder.Target;
            if (target.Branch is { } branch)
            {
                await storage.Engine.CheckoutBranchAsync(target.Repo, branch, progress, context.CancellationToken).ConfigureAwait(false);
            }
            else
            {
                await storage.Engine.CheckoutCommitAsync(target.Repo, target.Commit, progress, context.CancellationToken).ConfigureAwait(false);
            }

            done = offset + last;
        }
    }

    private void Report(JobContext context, string gameId, JobProgress progress)
    {
        context.Progress.Report(progress);
        _book.TryChange(gameId, context.JobId, record => record.Info.State is SessionState.Preparing or SessionState.Switching ? new SessionState.Switching(progress) : null);
    }

    // The profile's launch time is a courtesy: a catalog write that fails must not fail a launch that worked.
    private async Task RecordLaunchAsync(string profileId, DateTimeOffset startedAt)
    {
        try
        {
            await _services.Catalog.UpdateProfileAsync(profileId, profile => profile with { LastLaunchedAt = startedAt }).ConfigureAwait(false);
        }
        catch (Exception e) when (e is SavepointException or IOException)
        {
            // Left as it was.
        }
    }
}
