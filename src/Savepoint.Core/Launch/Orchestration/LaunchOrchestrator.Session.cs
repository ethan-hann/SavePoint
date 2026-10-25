using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Services;
using Savepoint.Core.Services.History;
using Savepoint.Core.Services.Profiles;

namespace Savepoint.Core.Launch.Orchestration;

public sealed partial class LaunchOrchestrator
{
    /// <inheritdoc />
    public Task<OperationResult<Unit>> MarkFinishedAsync(string gameId) =>
        ApiGuard.RunAsync(_services.Log, () =>
        {
            var record = _book.Record(gameId);
            if (record is null || !record.IsOpen)
            {
                throw new NoActiveSessionException(gameId);
            }

            // Ending the wait does not stop the game; the session moves to Exited when the watcher reports it.
            switch (record.Info.State)
            {
                case SessionState.Running:
                    record.Watcher?.MarkFinished();
                    break;
                case SessionState.Exited or SessionState.Completing:
                    break;
                default:
                    throw new NoActiveSessionException(gameId);
            }

            return Task.FromResult(Unit.Value);
        });

    // A session ends when the game does, however that is noticed, or when the player says so.
    private async Task FollowGameAsync(string gameId, Guid launchJobId, GameSessionWatcher watcher)
    {
        SessionOutcome? outcome = null;
        try
        {
            outcome = await watcher.Outcome.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The watcher was let go because a newer launch replaced this session.
            return;
        }
        catch (Exception ex)
        {
            // The process could not be read; the session ends as of now.
            var started = watcher.StartedPid is { } pid ? $"started pid {pid}" : "started no program";
            _services.Log.Warning($"Launch session for {gameId} (job {launchJobId}): could not be followed; {started}: {ex.Message}", ex);
        }

        if (outcome is not null)
        {
            var started = outcome.StartedPid is { } pid ? $"started pid {pid}" : "started no program";
            var followed = outcome.FollowedPids.Count > 0 ? string.Join(", ", outcome.FollowedPids) : "none";
            _services.Log.Information($"Launch session for {gameId} (job {launchJobId}): ended as {outcome.Kind}; {started}, followed pids {followed}.");
        }

        var endedAt = outcome?.EndedAt ?? _services.Time.GetUtcNow();
        var exited = await DescribeExitAsync(gameId, watcher.StartedAt, endedAt - watcher.StartedAt, outcome?.Kind ?? SessionEndKind.ChildExited).ConfigureAwait(false);
        _book.TryChange(gameId, launchJobId, record => record.Info.State is SessionState.Running ? exited : null);
    }

    // Counting never stops a session from ending: whatever cannot be read is left at nothing.
    private async Task<SessionState.Exited> DescribeExitAsync(string gameId, DateTimeOffset startedAt, TimeSpan duration, SessionEndKind endKind)
    {
        var info = _book.Get(gameId);
        var starts = _book.Record(gameId)?.Starts ?? new Dictionary<RepoRole, FolderStart>();
        var older = info is { StartedFromOlderPoint: true };
        Profile? profile = null;
        try
        {
            return await _services.Storage.ReadAsync(async storage =>
            {
                var game = await RequireGameAsync(gameId).ConfigureAwait(false);
                var profiles = await _services.Catalog.ListProfilesAsync(gameId).ConfigureAwait(false);
                profile = profiles.FirstOrDefault(listed => listed.Id == info?.ProfileId);
                var repos = GameFolders.Repos(_services, storage, game);
                var changed = await Task.WhenAll(repos.Select(repo => ChangedFilesAsync(storage, repo, profile, older ? null : starts.GetValueOrDefault(repo.Role)))).ConfigureAwait(false);
                var counts = repos.Zip(changed, (repo, count) => (repo.Role, count)).ToDictionary(pair => pair.Role, pair => pair.count);

                // A folder with nothing unsaved is not written by a replacement.
                var options = await OptionsAsync(storage, repos, profile, profiles, older, starts, counts.Where(pair => pair.Value > 0).Select(pair => pair.Key).ToHashSet()).ConfigureAwait(false);
                return new SessionState.Exited(startedAt, duration, counts.GetValueOrDefault(RepoRole.External), counts.GetValueOrDefault(RepoRole.Install), options, endKind);
            }).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            // Whatever went wrong reading the folders, the game has ended and the session must say so.
            return new SessionState.Exited(startedAt, duration, 0, 0, FallbackOptions(older, profile, starts), endKind);
        }
    }

    // Files that are new or changed in a folder since the session began; the folders were clean when it started. A file nobody can read
    // counts too: it may have changed, and the save will say so. A folder the launch left alone, which the profile has saved before and
    // which sits on another profile's history, is skipped by a new point and by an update; "skipped" is its start when those are the
    // choices, and its files are not on offer.
    private static async Task<int> ChangedFilesAsync(StorageSnapshot storage, RepoRef repo, Profile? profile, FolderStart? skipped)
    {
        var status = await storage.Engine.GetStatusAsync(repo).ConfigureAwait(false);
        if (skipped is { Left: true } && profile is not null
            && (status.IsDetached || !string.Equals(status.Branch, profile.BranchName, StringComparison.Ordinal))
            && await storage.Engine.BranchExistsAsync(repo, profile.BranchName).ConfigureAwait(false))
        {
            return 0;
        }

        return status.ChangedPaths.Count + status.UntrackedPaths.Count + status.HeldFileCount;
    }

    // What the player may do with the session (D14): a session from an older point forks or moves the profile forward.
    private static async Task<PostPlayOptions> OptionsAsync(
        StorageSnapshot storage,
        IReadOnlyList<RepoRef> repos,
        Profile? profile,
        IReadOnlyList<Profile> profiles,
        bool older,
        IReadOnlyDictionary<RepoRole, FolderStart> starts,
        IReadOnlySet<RepoRole> written)
    {
        if (profile is null)
        {
            return FallbackOptions(older, null, starts);
        }

        if (older)
        {
            // Continuing moves the profile's branch to where the folder began, so every folder the launch left alone has to have begun on it.
            var newer = new HashSet<string>(StringComparer.Ordinal);
            var canContinue = true;
            foreach (var repo in repos.Where(repo => starts.ContainsKey(repo.Role)))
            {
                var start = starts[repo.Role];
                var lineage = await storage.Engine.ListCommitsAsync(repo, profile.BranchName).ConfigureAwait(false);
                var index = lineage.ToList().FindIndex(commit => commit.Id == start.Commit);
                canContinue &= !start.Left || index >= 0;
                newer.UnionWith(lineage.Take(Math.Max(0, index)).Where(commit => commit.CaptureId is not null).Select(commit => commit.CaptureId!));
            }

            CompletionChoice[] choices = canContinue
                ? [CompletionChoice.ForkNewProfile, CompletionChoice.MoveForward, CompletionChoice.Skip]
                : [CompletionChoice.ForkNewProfile, CompletionChoice.Skip];
            return new PostPlayOptions(choices, CompletionChoice.ForkNewProfile, null, newer.Count);
        }

        // Replace is offered for the newest point History lists under this profile, and only when rewriting it cannot touch history another
        // profile holds (ReplaceRule). With nothing unsaved there is nothing to replace it with, and the cheap checks run before any grouping.
        if (written.Count == 0)
        {
            return FallbackOptions(older: false, profile, starts);
        }

        var owned = await HistoryReader.NewestOwnedAsync(storage, repos, profiles, profile.Id, CancellationToken.None).ConfigureAwait(false);
        if (owned is null || !await CanReplaceAsync(storage, repos, profiles, profile, owned, written).ConfigureAwait(false))
        {
            return FallbackOptions(older: false, profile, starts);
        }

        return new PostPlayOptions(
            [CompletionChoice.CaptureNew, CompletionChoice.UpdateExisting, CompletionChoice.Skip],
            CompletionChoice.CaptureNew,
            new SaveEntry(new HistoryPointId(owned.CaptureId), owned.Name, owned.At, IsMostRecent: true),
            0);
    }

    private static async Task<bool> CanReplaceAsync(
        StorageSnapshot storage,
        IReadOnlyList<RepoRef> repos,
        IReadOnlyList<Profile> profiles,
        Profile profile,
        HistoryReader.OwnedPoint owned,
        IReadOnlySet<RepoRole> written)
    {
        var others = profiles.Where(listed => listed.Id != profile.Id).Select(listed => listed.BranchName).ToList();
        var judged = new List<ReplaceFolder>();
        foreach (var repo in repos.Where(repo => written.Contains(repo.Role)))
        {
            judged.Add(await ReplaceRule.JudgeFolderAsync(storage.Engine, repo, profile.BranchName, others, owned.CaptureId, CancellationToken.None).ConfigureAwait(false));
        }

        return ReplaceRule.Allows(judged);
    }

    // The choices when the folders cannot be read. "Continue" needs every folder the launch left alone to have begun on the profile's own
    // branch, which the starts recorded at launch say without a read; a profile that is gone cannot be continued at all.
    private static PostPlayOptions FallbackOptions(bool older, Profile? profile, IReadOnlyDictionary<RepoRole, FolderStart> starts)
    {
        if (!older)
        {
            return new PostPlayOptions([CompletionChoice.CaptureNew, CompletionChoice.Skip], CompletionChoice.CaptureNew, null, 0);
        }

        var canContinue = profile is not null && starts.Values.All(start => !start.Left || string.Equals(start.Branch, profile.BranchName, StringComparison.Ordinal));
        CompletionChoice[] choices = canContinue
            ? [CompletionChoice.ForkNewProfile, CompletionChoice.MoveForward, CompletionChoice.Skip]
            : [CompletionChoice.ForkNewProfile, CompletionChoice.Skip];
        return new PostPlayOptions(choices, CompletionChoice.ForkNewProfile, null, 0);
    }

    // A retry's result joins what the folders that already saved reported, so the card always speaks for the whole save.
    private static CaptureResult? Joined(CaptureResult? before, CaptureResult? now)
    {
        if (before is null || now is null)
        {
            return now ?? before;
        }

        var kept = before.Folders.Where(folder => folder.Ok && now.Folders.All(other => other.Role != folder.Role));
        return new CaptureResult(now.ProfileId, now.PointId ?? before.PointId, [.. kept.Concat(now.Folders).OrderBy(folder => folder.Role)]);
    }

    // A launch or capture job that ends moves its session: the launch job failing or being cancelled ends the session as Failed,
    // and the capture job after play ends it as Completed. Both read the job's own outcome, so the card and the job agree.
    private void OnJobChanged(object? sender, JobSnapshot job)
    {
        if (job.Status == JobStatus.Running)
        {
            return;
        }

        _book.TryChangeByJob(job.JobId, record =>
        {
            switch (record.Info.State)
            {
                case SessionState.Preparing or SessionState.Switching when record.Info.LaunchJobId == job.JobId && job.Status != JobStatus.Succeeded:
                    return new SessionState.Failed(job.Error ?? ErrorInfo.From(new InvalidOperationException("The launch ended without saying why.")));
                case SessionState.Completing completing when completing.JobId == job.JobId:
                    var failed = job.Status is JobStatus.Failed or JobStatus.Cancelled;
                    record.SavedSoFar = Joined(record.SavedSoFar, job.Result as CaptureResult);
                    return new SessionState.Completed(completing.Completion, record.SavedSoFar, failed ? job.Error : null);
                default:
                    return null;
            }
        });
    }
}
