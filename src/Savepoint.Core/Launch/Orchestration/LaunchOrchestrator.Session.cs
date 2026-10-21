using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Services;

namespace Savepoint.Core.Launch.Orchestration;

public sealed partial class LaunchOrchestrator
{
    /// <inheritdoc />
    public Task<OperationResult<Unit>> MarkFinishedAsync(string gameId) =>
        ApiGuard.RunAsync(() =>
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
        catch (Exception)
        {
            // The process could not be read; the session ends as of now.
        }

        var endedAt = outcome?.EndedAt ?? _services.Time.GetUtcNow();
        var exited = await DescribeExitAsync(gameId, watcher.StartedAt, endedAt - watcher.StartedAt).ConfigureAwait(false);
        _book.TryChange(gameId, launchJobId, record => record.Info.State is SessionState.Running ? exited : null);
    }

    // Counting never stops a session from ending: whatever cannot be read is left at nothing.
    private async Task<SessionState.Exited> DescribeExitAsync(string gameId, DateTimeOffset startedAt, TimeSpan duration)
    {
        var info = _book.Get(gameId);
        var starts = _book.Record(gameId)?.StartCommits ?? new Dictionary<RepoRole, string>();
        var older = info is { StartedFromOlderPoint: true };
        try
        {
            return await _services.Storage.ReadAsync(async storage =>
            {
                var game = await RequireGameAsync(gameId).ConfigureAwait(false);
                var profiles = await _services.Catalog.ListProfilesAsync(gameId).ConfigureAwait(false);
                var repos = GameFolders.Repos(_services, storage, game);
                var changed = await Task.WhenAll(repos.Select(repo => ChangedFilesAsync(storage, repo))).ConfigureAwait(false);
                var options = await OptionsAsync(storage, repos, profiles.FirstOrDefault(profile => profile.Id == info?.ProfileId), older, starts).ConfigureAwait(false);
                var counts = repos.Zip(changed, (repo, count) => (repo.Role, count)).ToDictionary(pair => pair.Role, pair => pair.count);
                return new SessionState.Exited(startedAt, duration, counts.GetValueOrDefault(RepoRole.External), counts.GetValueOrDefault(RepoRole.Install), options);
            }).ConfigureAwait(false);
        }
        catch (Exception e) when (e is SavepointException or IOException)
        {
            return new SessionState.Exited(startedAt, duration, 0, 0, FallbackOptions(older));
        }
    }

    // Files that are new or changed in a folder since the session began; the folders were clean when it started.
    private static async Task<int> ChangedFilesAsync(StorageSnapshot storage, RepoRef repo)
    {
        var status = await storage.Engine.GetStatusAsync(repo).ConfigureAwait(false);
        return status.ChangedPaths.Count + status.UntrackedPaths.Count;
    }

    // What the player may do with the session (D14): a session from an older point forks or moves the profile forward.
    private static async Task<PostPlayOptions> OptionsAsync(
        StorageSnapshot storage,
        IReadOnlyList<RepoRef> repos,
        Profile? profile,
        bool older,
        IReadOnlyDictionary<RepoRole, string> starts)
    {
        if (profile is null)
        {
            return FallbackOptions(older);
        }

        if (older)
        {
            var newer = new HashSet<string>(StringComparer.Ordinal);
            foreach (var repo in repos.Where(repo => starts.ContainsKey(repo.Role)))
            {
                var lineage = await storage.Engine.ListCommitsAsync(repo, profile.BranchName).ConfigureAwait(false);
                var index = lineage.ToList().FindIndex(commit => commit.Id == starts[repo.Role]);
                newer.UnionWith(lineage.Take(Math.Max(0, index)).Where(commit => commit.CaptureId is not null).Select(commit => commit.CaptureId!));
            }

            return new PostPlayOptions([CompletionChoice.ForkNewProfile, CompletionChoice.MoveForward, CompletionChoice.Skip], CompletionChoice.ForkNewProfile, null, newer.Count);
        }

        // The save "update" would replace is the newest point of the saves folder, or of the install when the game has no saves folder.
        var folder = repos.FirstOrDefault(repo => repo.Role == RepoRole.External) ?? repos[0];
        var latest = (await storage.Engine.ListCommitsAsync(folder, profile.BranchName).ConfigureAwait(false)).FirstOrDefault(commit => commit.CaptureId is not null);
        return latest is null
            ? FallbackOptions(older: false)
            : new PostPlayOptions(
                [CompletionChoice.CaptureNew, CompletionChoice.UpdateExisting, CompletionChoice.Skip],
                CompletionChoice.CaptureNew,
                new SaveEntry(new HistoryPointId(latest.CaptureId!), latest.Subject, latest.Date, IsMostRecent: true),
                0);
    }

    private static PostPlayOptions FallbackOptions(bool older) =>
        older
            ? new PostPlayOptions([CompletionChoice.ForkNewProfile, CompletionChoice.MoveForward, CompletionChoice.Skip], CompletionChoice.ForkNewProfile, null, 0)
            : new PostPlayOptions([CompletionChoice.CaptureNew, CompletionChoice.Skip], CompletionChoice.CaptureNew, null, 0);

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
