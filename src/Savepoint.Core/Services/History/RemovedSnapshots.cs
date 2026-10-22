using System.Collections.Concurrent;
using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Services.History;

/// <summary>
/// Reads a game's recently removed snapshots back from the records the engine keeps (see <see cref="GitEngine.ListRemovedAsync"/>). The
/// commits the two folders removed in one capture event are one snapshot, as in History, and a snapshot belongs to no profile.
/// </summary>
internal sealed class RemovedSnapshots
{
    // As many git processes at once as the History reader uses.
    private const int _parallelism = 6;

    private readonly HistoryReader _history;

    public RemovedSnapshots(HistoryReader history)
    {
        _history = history;
    }

    /// <summary>One snapshot that left a profile: its commit in each folder that recorded it, whether or not a profile holds that commit again.</summary>
    /// <param name="CaptureId">The capture event, which is the snapshot's id.</param>
    /// <param name="Install">What the install folder recorded for it, or null.</param>
    /// <param name="External">What the saves folder recorded for it, or null.</param>
    internal sealed record Group(string CaptureId, RemovedCommit? Install, RemovedCommit? External)
    {
        /// <summary>The commit the snapshot is described by, as History describes a point by its install commit when it has one.</summary>
        public RemovedCommit Lead => Install ?? External!;

        public DateTimeOffset At => Lead.Commit.PointTime;

        public DateTimeOffset RemovedAt => Install is { } install && External is { } external
            ? (install.RemovedAt > external.RemovedAt ? install.RemovedAt : external.RemovedAt)
            : Lead.RemovedAt;

        public RemovedCommit? In(RepoRole role) => role == RepoRole.Install ? Install : External;
    }

    /// <summary>The snapshots listed, newest removal first, and every recorded commit of each folder (held by a profile again or not), which a snapshot's missing folder is resolved against.</summary>
    internal sealed record State(IReadOnlyList<Group> Groups, IReadOnlyDictionary<RepoRole, IReadOnlyList<RemovedCommit>> ByFolder);

    /// <summary>How many snapshots a game's folders keep as removed: what Free up space would delete for good.</summary>
    public static async Task<int> CountAsync(StorageSnapshot storage, IReadOnlyList<RepoRef> repos, CancellationToken cancellationToken) =>
        (await ReadStateAsync(storage, repos, cancellationToken).ConfigureAwait(false)).Groups.Count;

    /// <summary>
    /// Reads every folder's records and groups them into snapshots by capture event. A snapshot is listed while any of its recorded
    /// commits is on no branch; each folder's commit comes from that folder's own record. Needs only the git directories.
    /// </summary>
    public static async Task<State> ReadStateAsync(StorageSnapshot storage, IReadOnlyList<RepoRef> repos, CancellationToken cancellationToken)
    {
        var reads = await Task.WhenAll(repos.Select(async repo => (repo.Role, Removed: await storage.Engine.ListRemovedAsync(repo, cancellationToken).ConfigureAwait(false)))).ConfigureAwait(false);
        var byFolder = reads.ToDictionary(read => read.Role, read => read.Removed);

        // One entry per folder and capture event; a folder that somehow removed two commits of one event keeps the later removal.
        RemovedCommit? Pick(RepoRole role, string captureId) =>
            byFolder.GetValueOrDefault(role)?.Where(removed => removed.Commit.CaptureId == captureId).MaxBy(removed => removed.RemovedAt);

        var groups = byFolder.Values
            .SelectMany(removed => removed)
            .GroupBy(removed => removed.Commit.CaptureId!, StringComparer.Ordinal)
            .Where(records => records.Any(removed => !removed.OnBranch))
            .Select(records => new Group(records.Key, Pick(RepoRole.Install, records.Key), Pick(RepoRole.External, records.Key)))
            .OrderByDescending(group => group.RemovedAt)
            .ThenByDescending(group => group.At)
            .ToList();
        return new State(groups, byFolder);
    }

    public async Task<IReadOnlyList<RemovedSnapshot>> ReadAsync(
        StorageSnapshot storage,
        GameDefinition game,
        IReadOnlyList<RepoRef> repos,
        IReadOnlyList<Profile> profiles,
        CancellationToken cancellationToken)
    {
        var state = await ReadStateAsync(storage, repos, cancellationToken).ConfigureAwait(false);
        if (state.Groups.Count == 0)
        {
            return [];
        }

        var install = repos.Single(repo => repo.Role == RepoRole.Install);
        var candidates = new ConcurrentDictionary<(RepoRole, string), Lazy<Task<IReadOnlyList<CommitInfo>>>>();
        var snapshots = new RemovedSnapshot[state.Groups.Count];
        using var slots = new SemaphoreSlim(_parallelism);
        await Task.WhenAll(state.Groups.Select(async (group, index) =>
        {
            await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var origin = profiles.FirstOrDefault(profile => profile.BranchName == group.Lead.Branch);
                var commit = await CommitInAsync(storage, install, state, group, candidates, cancellationToken).ConfigureAwait(false);
                var installState = await _history.InstallStateAsync(game, install, origin?.VersionOverride, commit?.Id, cancellationToken).ConfigureAwait(false);
                snapshots[index] = new RemovedSnapshot(
                    new HistoryPointId(group.CaptureId),
                    (group.Install, group.External) switch
                    {
                        (not null, not null) => HistoryPointKind.VersionModsAndSave,
                        (not null, null) => HistoryPointKind.VersionAndMods,
                        _ => HistoryPointKind.SaveOnly,
                    },
                    group.Lead.Commit.Subject,
                    group.At,
                    installState,
                    origin?.Id,
                    origin?.Name,
                    group.RemovedAt,
                    group.Lead.Reason == RemovalReason.Replaced ? RemovedSnapshotReason.ReplacedWithSession : RemovedSnapshotReason.ContinuedFromSession);
            }
            finally
            {
                slots.Release();
            }
        })).ConfigureAwait(false);
        return snapshots;
    }

    /// <summary>
    /// The commit that stands for a snapshot in a folder: the one the snapshot recorded there, whether or not a profile holds it again,
    /// or, for a folder it did not record, the latest commit with a capture id made at or before it in the history of the profile it
    /// came from, counting both what that profile still holds and every commit that was recorded as taken off it. Null when there is
    /// none, which leaves nothing for that folder to start from.
    /// </summary>
    public static async Task<CommitInfo?> CommitInAsync(
        StorageSnapshot storage,
        RepoRef repo,
        State state,
        Group group,
        ConcurrentDictionary<(RepoRole, string), Lazy<Task<IReadOnlyList<CommitInfo>>>>? candidates,
        CancellationToken cancellationToken)
    {
        if (group.In(repo.Role) is { } own)
        {
            return own.Commit;
        }

        var branch = group.Lead.Branch;
        Task<IReadOnlyList<CommitInfo>> Read() => ReadCandidatesAsync(storage, repo, state, branch, cancellationToken);
        var history = candidates is null ? await Read().ConfigureAwait(false) : await candidates.GetOrAdd((repo.Role, branch), _ => new Lazy<Task<IReadOnlyList<CommitInfo>>>(Read)).Value.ConfigureAwait(false);
        return PointCommits.For(history, group.CaptureId, group.At);
    }

    // What the profile's branch holds in this folder together with every commit recorded as taken off that branch, including the ones a
    // profile holds again: the history the snapshot was taken in.
    private static async Task<IReadOnlyList<CommitInfo>> ReadCandidatesAsync(StorageSnapshot storage, RepoRef repo, State state, string branch, CancellationToken cancellationToken)
    {
        var tips = new List<string>();
        if (await storage.Engine.GetBranchTipAsync(repo, branch, cancellationToken).ConfigureAwait(false) is { } tip)
        {
            tips.Add(tip);
        }

        tips.AddRange(state.ByFolder.GetValueOrDefault(repo.Role, []).Where(removed => removed.Branch == branch).Select(removed => removed.Commit.Id));
        return await storage.Engine.ListCommitsFromAsync(repo, tips, cancellationToken).ConfigureAwait(false);
    }
}
