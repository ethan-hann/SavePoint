using System.Collections.Concurrent;
using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Services.History;

/// <summary>
/// Reads a game's recently removed snapshots back from the records the engine keeps (see <see cref="GitEngine.ListRemovedAsync(RepoRef, IReadOnlyCollection{string}, CancellationToken)"/>). The
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
    /// <param name="Mods">What the mods folder recorded for it, or null.</param>
    internal sealed record Group(string CaptureId, RemovedCommit? Install, RemovedCommit? External, RemovedCommit? Mods = null)
    {
        /// <summary>The commit the snapshot is described by, as History describes a point by its install commit, then its mods commit, when it has one.</summary>
        public RemovedCommit Lead => Install ?? Mods ?? External!;

        public DateTimeOffset At => Lead.Commit.PointTime;

        public DateTimeOffset RemovedAt => new[] { Install, Mods, External }.OfType<RemovedCommit>().Max(removed => removed.RemovedAt);

        public RemovedCommit? In(RepoRole role) => role switch
        {
            RepoRole.Install => Install,
            RepoRole.Mods => Mods,
            RepoRole.External => External,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Not one of a game's folders."),
        };
    }

    /// <summary>The snapshots listed, newest removal first, and every recorded commit of each folder (held by a profile again or not), which a snapshot's missing folder is resolved against.</summary>
    internal sealed record State(IReadOnlyList<Group> Groups, IReadOnlyDictionary<RepoRole, IReadOnlyList<RemovedCommit>> ByFolder);

    /// <summary>
    /// What Free up space would delete for a game: how many snapshots its folders keep as removed, and every record of those snapshots
    /// in either folder, so the run can delete those and no others. A branch no profile names goes with them only when every snapshot
    /// it holds on its own is one of those, because such a branch would keep their space from being freed; one that holds any other
    /// snapshot is never named.
    /// </summary>
    public static async Task<FreeUpSpacePreview> PreviewAsync(StorageSnapshot storage, string gameId, IReadOnlyList<RepoRef> repos, IReadOnlyList<Profile> profiles, CancellationToken cancellationToken)
    {
        var state = await ReadStateAsync(storage, repos, profiles, cancellationToken).ConfigureAwait(false);
        var listed = state.Groups.Select(group => group.CaptureId).ToHashSet(StringComparer.Ordinal);
        var records = state.ByFolder
            .SelectMany(folder => folder.Value
                .Where(removed => listed.Contains(removed.Commit.CaptureId!))
                .Select(removed => new FreeUpSpaceRecord(gameId, folder.Key, removed.RefName, removed.Commit.Id)))
            .ToList();

        if (listed.Count > 0)
        {
            var counted = profiles.Select(profile => profile.BranchName).ToList();
            foreach (var repo in repos)
            {
                foreach (var stray in await storage.Engine.ListStrayBranchesAsync(repo, counted, cancellationToken).ConfigureAwait(false))
                {
                    var snapshots = stray.OnlyHere.Where(commit => commit.CaptureId is not null).ToList();
                    if (snapshots.Count > 0 && snapshots.All(commit => listed.Contains(commit.CaptureId!)))
                    {
                        records.Add(new FreeUpSpaceRecord(gameId, repo.Role, GitDirFiles.BranchPrefix + stray.Name, stray.Tip));
                    }
                }
            }
        }

        return new FreeUpSpacePreview(state.Groups.Count, new FreeUpSpaceToken(records));
    }

    /// <summary>A gate that lets as many folders be measured at once as the listing reads at once; share it across the games of one estimate.</summary>
    public static SemaphoreSlim NewEstimateGate() => new(_parallelism);

    /// <summary>
    /// About how many bytes deleting the token's records for the game would free: the stored size of what only they reach, summed over the
    /// game's folders, side by side. Records of other games, and of folders the game no longer has, count nothing. The records a run that was
    /// cut off still owns count as dropped too, since the next run finishes them first. Needs only the git directories.
    /// <paramref name="walks"/> bounds how many folders are measured at once; pass one gate to every call of an all-games estimate so the
    /// bound holds across games, or leave it null for a bound of this game's own.
    /// </summary>
    public static async Task<long> EstimateAsync(StorageSnapshot storage, IReadOnlyList<RepoRef> repos, FreeUpSpaceToken token, CancellationToken cancellationToken, SemaphoreSlim? walks = null)
    {
        using var own = walks is null ? new SemaphoreSlim(_parallelism) : null;
        walks ??= own!;
        // A run that was cut off finishes before the next one starts, so what its note names is freed too and must not count as kept.
        var pending = repos.Count == 0 ? null : FreeUpIntent.Read(storage.GameDirectory(repos[0].GameId));

        // The folders are separate repositories, so they are measured side by side.
        var sizes = await Task.WhenAll(repos.Select(async repo =>
        {
            var refs = token.Records
                .Where(record => record.GameId == repo.GameId && record.Folder == repo.Role)
                .Select(record => new ClearedRef(record.RefName, record.Value))
                .ToList();
            if (refs.Count == 0)
            {
                return 0L;
            }

            if (pending is not null && pending.TryGetValue(repo.Role, out var owned))
            {
                refs.AddRange(owned);
            }

            await walks.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await storage.Engine.EstimateFreedBytesAsync(repo, refs, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                walks.Release();
            }
        })).ConfigureAwait(false);
        return sizes.Sum();
    }

    /// <summary>
    /// Reads every folder's records and groups them into snapshots by capture event. A snapshot is listed while any of its recorded
    /// commits is on no profile's branch (a branch no profile names does not count); each folder's commit comes from that folder's own
    /// record. Needs only the git directories.
    /// </summary>
    public static async Task<State> ReadStateAsync(StorageSnapshot storage, IReadOnlyList<RepoRef> repos, IReadOnlyList<Profile> profiles, CancellationToken cancellationToken)
    {
        var counted = profiles.Select(profile => profile.BranchName).ToList();
        var reads = await Task.WhenAll(repos.Select(async repo => (repo.Role, Removed: await storage.Engine.ListRemovedAsync(repo, counted, cancellationToken).ConfigureAwait(false)))).ConfigureAwait(false);

        // A free-up that was cut off has deleted some of its records and will delete the rest; none of them is listed meanwhile, so no
        // snapshot shows with only some of its folders.
        IReadOnlySet<string> pending = repos.Count == 0 ? new HashSet<string>() : FreeUpIntent.NamedRefs(storage.GameDirectory(repos[0].GameId));
        var byFolder = reads.ToDictionary(read => read.Role, read => (IReadOnlyList<RemovedCommit>)[.. read.Removed.Where(removed => !pending.Contains(removed.RefName))]);

        // One entry per folder and capture event; a folder that somehow removed two commits of one event keeps the later removal.
        RemovedCommit? Pick(RepoRole role, string captureId) =>
            byFolder.GetValueOrDefault(role)?.Where(removed => removed.Commit.CaptureId == captureId).MaxBy(removed => removed.RemovedAt);

        var groups = byFolder.Values
            .SelectMany(removed => removed)
            .GroupBy(removed => removed.Commit.CaptureId!, StringComparer.Ordinal)
            .Where(records => records.Any(removed => !removed.OnBranch))
            .Select(records => new Group(records.Key, Pick(RepoRole.Install, records.Key), Pick(RepoRole.External, records.Key), Pick(RepoRole.Mods, records.Key)))
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
        var state = await ReadStateAsync(storage, repos, profiles, cancellationToken).ConfigureAwait(false);
        if (state.Groups.Count == 0)
        {
            return [];
        }

        var install = repos.Single(repo => repo.Role == RepoRole.Install);
        var mods = repos.FirstOrDefault(repo => repo.Role == RepoRole.Mods);
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
                var modsCommit = mods is null ? null : await CommitInAsync(storage, mods, state, group, candidates, cancellationToken).ConfigureAwait(false);
                var installState = await _history.InstallStateAsync(game, install, origin?.VersionOverride, commit?.Id, mods, modsCommit?.Id, cancellationToken).ConfigureAwait(false);
                snapshots[index] = new RemovedSnapshot(
                    new HistoryPointId(group.CaptureId),
                    (group.Install ?? group.Mods, group.External) switch
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
