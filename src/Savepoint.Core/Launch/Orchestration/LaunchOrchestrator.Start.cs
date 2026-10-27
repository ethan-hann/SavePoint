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
    public Task<OperationResult<JobStarted>> StartAsync(string gameId, LaunchTarget target, bool startSteam = false, bool withoutSteam = false) =>
        ApiGuard.RunAsync(_services.Log, async () =>
        {
            ArgumentNullException.ThrowIfNull(target);
            var game = await RequireGameAsync(gameId).ConfigureAwait(false);

            // The claim makes the check and the session one step: a second call made at the same moment finds the game taken.
            if (!_book.TryClaim(gameId))
            {
                throw TakenBy(game);
            }

            var opened = false;
            try
            {
                if (await IsGameOpenAsync(game).ConfigureAwait(false))
                {
                    throw new LaunchAlreadyRunningException(gameId);
                }

                // Asked here as well as in the job: a refusal now has no job and no session, and nothing was read.
                if (!startSteam && !withoutSteam && await SteamIsMissingAsync(game).ConfigureAwait(false))
                {
                    throw new SteamNotRunningException(gameId);
                }

                var (profile, older) = await ResolveForSessionAsync(game, target).ConfigureAwait(false);
                var profileId = profile.Id;

                // The work waits until the session exists, so its first change always finds it.
                var sessionOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var jobId = _services.Jobs.Start(JobKind.Launch, gameId, cancellable: true, context => RunLaunchAsync(context, gameId, target, startSteam, withoutSteam, sessionOpen.Task), new JobProfile(profile.Id, profile.Name));
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

    /// <inheritdoc />
    public Task<OperationResult<JobStarted>> SwitchAsync(string gameId, LaunchTarget target) =>
        ApiGuard.RunAsync(_services.Log, async () =>
        {
            ArgumentNullException.ThrowIfNull(target);
            var game = await RequireGameAsync(gameId).ConfigureAwait(false);

            // The claim keeps the game taken for the whole job, so a launch or a second switch cannot begin while folders are changing.
            if (!_book.TryClaim(gameId, switching: true))
            {
                throw TakenBy(game);
            }

            var started = false;
            try
            {
                if (await IsGameOpenAsync(game).ConfigureAwait(false))
                {
                    throw new LaunchAlreadyRunningException(gameId);
                }

                var (profile, _) = await ResolveForSessionAsync(game, target).ConfigureAwait(false);
                var jobId = _services.Jobs.Start(JobKind.Switch, gameId, cancellable: true, context => RunSwitchAsync(context, gameId, target), new JobProfile(profile.Id, profile.Name));
                started = true;
                return new JobStarted(jobId);
            }
            finally
            {
                if (!started)
                {
                    _book.Unclaim(gameId);
                }
            }
        });

    private async Task<JobResult?> RunSwitchAsync(JobContext context, string gameId, LaunchTarget target)
    {
        try
        {
            await _services.RunForGameAsync(gameId, (storage, _) => PrepareAsync(storage, context, gameId, target), context.CancellationToken).ConfigureAwait(false);
            return null;
        }
        finally
        {
            _book.Unclaim(gameId);
        }
    }

    // A switch that holds the game says so; anything else that holds it is a game that is running or starting.
    private Exception TakenBy(GameDefinition game) =>
        _book.IsSwitching(game.Id) ? new SwitchInProgressException(game.Id, game.Name) : new LaunchAlreadyRunningException(game.Id);

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
    private async Task<(Profile Profile, bool Older)> ResolveForSessionAsync(GameDefinition game, LaunchTarget target)
    {
        var profiles = await _services.Catalog.ListProfilesAsync(game.Id).ConfigureAwait(false);
        return await _services.Storage.ReadAsync(async storage =>
        {
            var repos = GameFolders.Repos(_services, storage, game);
            var resolved = await TargetResolver.ResolveAsync(storage, repos, profiles, target, CancellationToken.None).ConfigureAwait(false);
            return (resolved.Profile, resolved.IsOlderPoint);
        }).ConfigureAwait(false);
    }

    private async Task<JobResult?> RunLaunchAsync(JobContext context, string gameId, LaunchTarget target, bool startSteam, bool withoutSteam, Task sessionOpen)
    {
        await sessionOpen.ConfigureAwait(false);

        // Before the first folder is read: a Steam game needs the client, and a client started later would update over the switched version.
        // The manifest is read before Steam can start, so a change made while it comes up shows against it.
        var steamGame = await RequireGameAsync(gameId, context.CancellationToken).ConfigureAwait(false);
        var steamBefore = withoutSteam ? null : await ReadSteamInstallAsync(steamGame).ConfigureAwait(false);
        if (!withoutSteam)
        {
            await EnsureSteamAsync(context, steamGame, startSteam).ConfigureAwait(false);
        }

        // Preparing and switching hold the game's lock, which a running optimization yields to (D13.17); the lock is let go before the game starts.
        var prepared = await _services.RunForGameAsync(gameId, (storage, _) => PrepareAsync(storage, context, gameId, target), context.CancellationToken).ConfigureAwait(false);
        context.CancellationToken.ThrowIfCancellationRequested();

        if (!withoutSteam)
        {
            await RefuseWhenSteamUpdatesAsync(prepared.Game, steamBefore, prepared.InstallPath).ConfigureAwait(false);
        }

        context.AddStep(JobSteps.LaunchStarting, ("game", prepared.Game.Name));
        var watcher = await _starter.StartAsync(prepared.Game.Launch, prepared.InstallPath, context.CancellationToken).ConfigureAwait(false);

        // The game is running, so it is tracked before anything else is attempted: nothing after this may lose it.
        // Subscribed before the state is set and read inside the change, so a quick exit is never missed in between.
        watcher.EarlyExitChanged += (_, _) => _book.TryChange(gameId, context.JobId, record =>
            record.Info.State is SessionState.Running current && current.ProgramExitCode != watcher.EarlyExitCode
                ? current with { ProgramExitCode = watcher.EarlyExitCode }
                : null);
        var running = _book.TryChange(gameId, context.JobId, record =>
        {
            record.Watcher = watcher;
            return new SessionState.Running(watcher.StartedAt, watcher.EarlyExitCode);
        });
        if (!running)
        {
            watcher.Dispose();
            return null;
        }

        _ = FollowGameAsync(gameId, context.JobId, watcher);
        await RecordLaunchAsync(prepared.Resolved.Profile.Id, watcher.StartedAt).ConfigureAwait(false);
        return null;
    }

    // Both folders are read before either is touched (D13.8): one with unsaved work stops the launch with nothing switched.
    private async Task<Prepared> PrepareAsync(StorageSnapshot storage, JobContext context, string gameId, LaunchTarget target)
    {
        var token = context.CancellationToken;
        var game = await RequireGameAsync(gameId, token).ConfigureAwait(false);

        // The wait for the lock can be long (a save, an optimization, a storage move); the player may have started the game meanwhile.
        if (await IsGameOpenAsync(game).ConfigureAwait(false))
        {
            throw new LaunchAlreadyRunningException(gameId);
        }

        var profiles = await _services.Catalog.ListProfilesAsync(gameId, token).ConfigureAwait(false);
        var repos = GameFolders.Repos(_services, storage, game);
        var resolved = await TargetResolver.ResolveAsync(storage, repos, profiles, target, token).ConfigureAwait(false);
        var readings = await Task.WhenAll(resolved.Folders.Select(folder => FolderReading.ReadAsync(storage.Engine, folder, profiles, countReplaced: false, token))).ConfigureAwait(false);

        // A half-switched folder is refused with its own error unless it is the very switch this launch would finish.
        if (readings.FirstOrDefault(reading => reading.Dirty) is { } blocked)
        {
            throw blocked.Blocked
                ? new InterruptedSwitchException(blocked.Target.Role, blocked.Note!.ToCommit)
                : new DirtyWorkingTreeException(blocked.Target.Role);
        }

        // Unsaved work in any folder came first, since that is what has to be resolved; a file that cannot be read stops the launch just as
        // certainly, and before either folder is switched.
        if (readings.FirstOrDefault(reading => reading.Held) is { } held)
        {
            throw held.Status.HeldFile!.ToException(HeldFileOperation.Switch);
        }

        // A folder that needs no switch is said so first; the folders that do are said as each one is switched.
        foreach (var reading in readings.Where(reading => !reading.NeedsSwitch))
        {
            if (reading.Target.Leave)
            {
                context.AddStep(JobSteps.LaunchFolderNotIncluded, ("folder", reading.Target.Role));
            }
            else
            {
                context.AddStep(JobSteps.LaunchFolderInPlace, ("folder", reading.Target.Role), ("profile", resolved.Profile.Name));
            }
        }

        var pending = readings.Where(reading => reading.NeedsSwitch).ToList();
        if (pending.Count > 0)
        {
            await SwitchAsync(storage, context, gameId, resolved.Profile.Name, pending).ConfigureAwait(false);
        }


        // The session opened with what was true before the wait for the lock; what the switch really started from is what counts.
        _book.TryUpdate(gameId, context.JobId, record =>
        {
            record.Info = record.Info with { ProfileId = resolved.Profile.Id, StartedFromOlderPoint = resolved.IsOlderPoint };
            record.Starts = readings.ToDictionary(
                reading => reading.Target.Role,
                reading => reading.Target.Leave ? new FolderStart(reading.Status.HeadCommit, Left: true, reading.Status.Branch) : new FolderStart(reading.Target.Commit, Left: false));
        });
        return new Prepared(game, repos.Single(repo => repo.Role == RepoRole.Install).WorkTree, resolved);
    }

    private async Task SwitchAsync(StorageSnapshot storage, JobContext context, string gameId, string profileName, IReadOnlyList<FolderReading> folders)
    {
        Report(context, gameId, JobProgress.None);

        // The folders after the first are asked before the first is switched: a refusal in the saves folder (room, case clashes, folder
        // links) must not come after the install folder is already on the new profile. The first folder's own switch refuses before it writes,
        // so only its growth is read here, and only when another folder is on its drive: each can fit alone and still not fit together.
        // The growth comes from the saved points, so the largest folder is not scanned a second time.
        var volumes = folders.Select(folder => _services.Disk.GetVolumeId(folder.Target.Repo.WorkTree)).ToList();
        var growth = new long[folders.Count];
        for (var i = 1; i < folders.Count; i++)
        {
            growth[i] = await CheckAsync(storage, folders[i], context.CancellationToken).ConfigureAwait(false);
        }

        if (volumes[0] is { } first && volumes.Skip(1).Contains(first))
        {
            var target = folders[0].Target;
            growth[0] = await storage.Engine.SwitchGrowthAsync(target.Repo, target.Branch ?? target.Commit, isBranch: target.Branch is not null, context.CancellationToken).ConfigureAwait(false);
        }

        foreach (var (repo, bytes) in SwitchRoom.Shared([.. folders.Select((folder, i) => new FolderGrowth(folder.Target.Repo, volumes[i], growth[i]))]))
        {
            storage.Engine.RequireSwitchRoom(repo, bytes);
        }

        var done = 0;
        var doneBytes = 0L;
        foreach (var folder in folders)
        {
            var offset = done;
            var bytesOffset = doneBytes;
            var last = 0;
            var lastBytes = 0L;
            var progress = new InlineProgress<FileProgress>(value =>
            {
                last = value.FilesDone;
                lastBytes = value.BytesDone ?? lastBytes;
                Report(
                    context,
                    gameId,
                    new JobProgress(
                        offset + value.FilesDone,
                        value.FilesTotal is { } total ? offset + total : null,
                        value.BytesDone is { } bytes ? bytesOffset + bytes : null,
                        value.BytesTotal is { } bytesTotal ? bytesOffset + bytesTotal : null,
                        value.CurrentPath,
                        value.Finishing));
            });
            var target = folder.Target;
            var counts = await SwitchCountsAsync(storage, folder, context.CancellationToken).ConfigureAwait(false);
            context.AddStep(
                JobSteps.LaunchSwitchFolder,
                ("folder", target.Role),
                ("profile", profileName),
                ("older", target.Branch is null ? "yes" : "no"),
                ("counted", counts is null ? "no" : "yes"),
                ("written", counts?.Written ?? 0),
                ("removed", counts?.Removed ?? 0));
            _services.Log.Information($"Switch: the {GameFolders.LogName(target.Role)} folder of {gameId} ({target.Repo.WorkTree}) from {folder.Status.HeadCommit ?? "(no point)"} to {target.Commit}{(target.Branch is { } named ? $" on {named}" : " (an older point)")}.");
            if (target.Branch is { } branch)
            {
                await storage.Engine.CheckoutBranchAsync(target.Repo, branch, progress, context.CancellationToken).ConfigureAwait(false);
            }
            else
            {
                await storage.Engine.CheckoutCommitAsync(target.Repo, target.Commit, progress, context.CancellationToken).ConfigureAwait(false);
            }

            done = offset + last;
            doneBytes = bytesOffset + lastBytes;
        }
    }

    private static Task<long> CheckAsync(StorageSnapshot storage, FolderReading folder, CancellationToken cancellationToken)
    {
        var target = folder.Target;
        return storage.Engine.CheckSwitchAsync(target.Repo, target.Branch ?? target.Commit, isBranch: target.Branch is not null, cancellationToken);
    }

    // How many files the switch writes and removes, for the job's details; a folder git cannot compare is left for the switch itself to report.
    private static async Task<(int Written, int Removed)?> SwitchCountsAsync(StorageSnapshot storage, FolderReading reading, CancellationToken cancellationToken)
    {
        try
        {
            var diff = await storage.Engine.SwitchStatAsync(reading.Target.Repo, SwitchOrigin(reading), reading.Target.Commit, cancellationToken).ConfigureAwait(false);
            return (diff.FilesChanged - diff.FilesRemoved, diff.FilesRemoved);
        }
        catch (SavepointException)
        {
            return null;
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
        catch (Exception e) when (e is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            // Left as it was: a file that is locked or read-only is not worth a launch that worked.
        }
    }
}
