using System.Collections.Concurrent;
using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Services.History;

/// <summary>
/// Reads a game's capture events back from its git directories (D15): the commits of one capture share a capture id, one per
/// folder, and that id is the point. Only the git directories are read, so history stays readable while a game folder has moved.
/// </summary>
internal sealed class HistoryReader
{
    // Several git processes at once is as many as a disk and a virus scanner serve better than one at a time.
    private const int _parallelism = 6;

    private readonly BackendServices _services;
    private readonly ConcurrentDictionary<string, int> _modsChanged = new(StringComparer.Ordinal);

    public HistoryReader(BackendServices services)
    {
        _services = services;
    }

    // One commit of a capture event, in the lineage of the profile that owns it.
    private sealed record Entry(RepoRole Role, Profile Profile, CommitInfo Commit, string? Previous);

    private sealed class RawPoint(string captureId)
    {
        public string CaptureId { get; } = captureId;

        public Entry? Install { get; set; }

        public Entry? Mods { get; set; }

        public Entry? External { get; set; }

        // The point is described by the install folder's commit, then the mods folder's, then the saves folder's.
        private Entry Lead => (Install ?? Mods ?? External)!;

        public Profile Owner => Lead.Profile;

        public DateTimeOffset At => Lead.Commit.PointTime;

        public string Name => Lead.Commit.Subject;
    }

    public async Task<GameHistory> ReadAsync(
        StorageSnapshot storage,
        GameDefinition game,
        IReadOnlyList<Profile> profiles,
        string? profileId,
        CancellationToken cancellationToken)
    {
        var repos = GameFolders.ReposForGitDirOnly(_services, storage, game);
        var install = repos.Single(repo => repo.Role == RepoRole.Install);
        var mods = repos.FirstOrDefault(repo => repo.Role == RepoRole.Mods);

        var lineages = await ReadLineagesAsync(storage, repos, profiles, cancellationToken).ConfigureAwait(false);
        var raw = Group(repos, profiles, lineages);
        var heads = await ReadHeadsAsync(storage, repos, cancellationToken).ConfigureAwait(false);

        var wanted = raw.Where(point => profileId is null || point.Owner.Id == profileId).ToList();
        var installAt = wanted.ToDictionary(point => point.CaptureId, point => CommitFor(point, point.Install, lineages, install));
        var modsAt = wanted.ToDictionary(point => point.CaptureId, point => mods is null ? null : CommitFor(point, point.Mods, lineages, mods));
        var points = new HistoryPoint[wanted.Count];
        using var slots = new SemaphoreSlim(_parallelism);
        await Task.WhenAll(wanted.Select(async (point, index) =>
        {
            await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                points[index] = await DescribeAsync(storage, game, install, mods, point, installAt[point.CaptureId], modsAt[point.CaptureId], heads, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                slots.Release();
            }
        })).ConfigureAwait(false);

        return new GameHistory(game.Id, profileId, [.. points.OrderByDescending(point => point.CapturedAt)]);
    }

    // How many points each profile owns, assigned as ReadAsync assigns them, so a profile's row shows the number History lists for it.
    public static async Task<IReadOnlyDictionary<string, int>> CountByOwnerAsync(
        StorageSnapshot storage,
        IReadOnlyList<RepoRef> repos,
        IReadOnlyList<Profile> profiles,
        CancellationToken cancellationToken)
    {
        var lineages = await ReadLineagesAsync(storage, repos, profiles, cancellationToken).ConfigureAwait(false);
        return Group(repos, profiles, lineages).GroupBy(point => point.Owner.Id).ToDictionary(group => group.Key, group => group.Count());
    }

    // Each profile's points in each folder, newest first; every (folder, profile) pair is one git call.
    private static async Task<Dictionary<(RepoRole, string), IReadOnlyList<CommitInfo>>> ReadLineagesAsync(
        StorageSnapshot storage,
        IReadOnlyList<RepoRef> repos,
        IReadOnlyList<Profile> profiles,
        CancellationToken cancellationToken)
    {
        using var slots = new SemaphoreSlim(_parallelism);
        var reads = repos.SelectMany(repo => profiles.Select(async profile =>
        {
            await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var commits = await storage.Engine.ListCommitsAsync(repo, profile.BranchName, null, cancellationToken).ConfigureAwait(false);
                return (Key: (repo.Role, profile.Id), Commits: commits);
            }
            finally
            {
                slots.Release();
            }
        })).ToList();
        return (await Task.WhenAll(reads).ConfigureAwait(false)).ToDictionary(read => read.Key, read => read.Commits);
    }

    /// <summary>A point as History reckons it for one profile: its capture id, name and time.</summary>
    internal sealed record OwnedPoint(string CaptureId, string Name, DateTimeOffset At);

    /// <summary>The newest point History lists under a profile (same grouping and ownership as <see cref="ReadAsync"/>), or null when it owns none.</summary>
    internal static async Task<OwnedPoint?> NewestOwnedAsync(
        StorageSnapshot storage,
        IReadOnlyList<RepoRef> repos,
        IReadOnlyList<Profile> profiles,
        string profileId,
        CancellationToken cancellationToken)
    {
        var lineages = await ReadLineagesAsync(storage, repos, profiles, cancellationToken).ConfigureAwait(false);
        var newest = Group(repos, profiles, lineages).Where(point => point.Owner.Id == profileId).OrderByDescending(point => point.At).FirstOrDefault();

        return newest is null ? null : new OwnedPoint(newest.CaptureId, newest.Name, newest.At);
    }

    // A commit belongs to the first profile (in catalog order) whose lineage holds it, so a duplicate shows only its own new points.
    private static List<RawPoint> Group(
        IReadOnlyList<RepoRef> repos,
        IReadOnlyList<Profile> profiles,
        Dictionary<(RepoRole, string), IReadOnlyList<CommitInfo>> lineages)
    {
        var byId = new Dictionary<string, RawPoint>(StringComparer.Ordinal);
        var order = new List<RawPoint>();
        foreach (var repo in repos)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var profile in profiles)
            {
                var points = lineages[(repo.Role, profile.Id)].Where(commit => commit.CaptureId is not null).ToList();
                for (var i = 0; i < points.Count; i++)
                {
                    var commit = points[i];
                    if (!seen.Add(commit.Id))
                    {
                        continue;
                    }

                    if (!byId.TryGetValue(commit.CaptureId!, out var point))
                    {
                        byId[commit.CaptureId!] = point = new RawPoint(commit.CaptureId!);
                        order.Add(point);
                    }

                    var entry = new Entry(repo.Role, profile, commit, i + 1 < points.Count ? points[i + 1].Id : null);
                    switch (repo.Role)
                    {
                        case RepoRole.Install:
                            point.Install ??= entry;
                            break;
                        case RepoRole.Mods:
                            point.Mods ??= entry;
                            break;
                        default:
                            point.External ??= entry;
                            break;
                    }
                }
            }
        }

        return order;
    }

    // The folder's current commit, from the git directory alone.
    private static async Task<Dictionary<RepoRole, string?>> ReadHeadsAsync(StorageSnapshot storage, IReadOnlyList<RepoRef> repos, CancellationToken cancellationToken)
    {
        var heads = new Dictionary<RepoRole, string?>();
        foreach (var repo in repos)
        {
            heads[repo.Role] = await HeadCommitAsync(storage, repo, cancellationToken).ConfigureAwait(false);
        }

        return heads;
    }

    private static async Task<string?> HeadCommitAsync(StorageSnapshot storage, RepoRef repo, CancellationToken cancellationToken)
    {
        try
        {
            var head = GitDirFiles.ReadHead(repo.GitDir);
            return head.IsDetached ? head.DetachedCommit : await storage.Engine.GetBranchTipAsync(repo, head.Branch!, cancellationToken).ConfigureAwait(false);
        }
        catch (GitOperationException)
        {
            return null;
        }
    }

    // The commit of one folder that describes the point: its own, or for a point that folder did not record the profile's point in that
    // folder current when it was made.
    private static string? CommitFor(
        RawPoint point,
        Entry? own,
        Dictionary<(RepoRole, string), IReadOnlyList<CommitInfo>> lineages,
        RepoRef repo)
    {
        if (own is not null)
        {
            return own.Commit.Id;
        }

        return PointCommits.For(lineages[(repo.Role, point.Owner.Id)], point.CaptureId, point.At)?.Id;
    }

    private async Task<HistoryPoint> DescribeAsync(
        StorageSnapshot storage,
        GameDefinition game,
        RepoRef install,
        RepoRef? mods,
        RawPoint point,
        string? installCommit,
        string? modsCommit,
        Dictionary<RepoRole, string?> heads,
        CancellationToken cancellationToken)
    {
        // A change only the mods folder recorded is still "mods changed" to the player, so it is not a kind of its own.
        var kind = (point.Install ?? point.Mods, point.External) switch
        {
            (not null, not null) => HistoryPointKind.VersionModsAndSave,
            (not null, null) => HistoryPointKind.VersionAndMods,
            _ => HistoryPointKind.SaveOnly,
        };

        var state = await InstallStateAsync(game, install, point.Owner.VersionOverride, installCommit, mods, modsCommit, cancellationToken).ConfigureAwait(false);

        // The mod-file count is only meaningful where the game names mod folders or has a mods folder; a first point has nothing to compare with.
        int? installChanged = point.Install is { Previous: { } before } own && game.ModPathHints.Count > 0
            ? await ModFilesChangedAsync(storage, install, before, own.Commit.Id, game.ModPathHints, cancellationToken).ConfigureAwait(false)
            : null;
        int? modsChangedInFolder = mods is not null && point.Mods is { Previous: { } modsBefore } modsOwn
            ? await FilesChangedInModsAsync(storage, mods, modsBefore, modsOwn.Commit.Id, cancellationToken).ConfigureAwait(false)
            : null;
        int? modsChanged = installChanged is null && modsChangedInFolder is null ? null : (installChanged ?? 0) + (modsChangedInFolder ?? 0);

        // A save-only point recorded no install of its own, so it has no size to show beside a version.
        var bytes = point.Install is { } recorded ? await InstallBytesAsync(install, recorded.Commit.Id, cancellationToken).ConfigureAwait(false) : null;

        // A folder the point did not record does not decide whether it is in use, but a point of the game's own folders needs one that did.
        var recordsGame = point.Install is not null || point.Mods is not null;
        return new HistoryPoint(
            new HistoryPointId(point.CaptureId),
            kind,
            point.Name,
            point.At,
            point.Owner.Id,
            point.Owner.Name,
            state,
            modsChanged,
            bytes,
            InUse: recordsGame
                && (point.Install is not { } heldInstall || heads.GetValueOrDefault(RepoRole.Install) == heldInstall.Commit.Id)
                && (point.Mods is not { } heldMods || heads.GetValueOrDefault(RepoRole.Mods) == heldMods.Commit.Id),
            LoadedNow: point.External is { } loaded && heads.GetValueOrDefault(RepoRole.External) == loaded.Commit.Id);
    }

    // What the game's own folders held at a commit: the owner's version override, else the version read from the game's program, and the
    // mod-file count of the install commit with the mods folder's commit beside it.
    internal async Task<InstallState> InstallStateAsync(
        GameDefinition game,
        RepoRef install,
        string? versionOverride,
        string? commit,
        RepoRef? modsRepo,
        string? modsCommit,
        CancellationToken cancellationToken)
    {
        if (commit is null)
        {
            return new InstallState(versionOverride, null);
        }

        try
        {
            var mods = await _services.Insights.ModFileCountAsync(install, commit, game.ModPathHints, modsRepo, modsCommit, cancellationToken).ConfigureAwait(false);
            var version = versionOverride ?? await _services.Insights.DetectedVersionAsync(install, commit, game.Launch, cancellationToken).ConfigureAwait(false);
            return new InstallState(version, mods);
        }
        catch (SavepointException)
        {
            // A point whose files cannot be read is still a point; what it held is left unsaid.
            return new InstallState(versionOverride, null);
        }
    }

    // How many of the files that changed between two install points are mod files; null when the comparison cannot be made.
    private async Task<int?> ModFilesChangedAsync(
        StorageSnapshot storage,
        RepoRef repo,
        string from,
        string to,
        IReadOnlyList<string> hints,
        CancellationToken cancellationToken)
    {
        var key = $"{repo.GitDir}|{from}|{to}|{string.Join('', hints)}";
        if (_modsChanged.TryGetValue(key, out var known))
        {
            return known;
        }

        try
        {
            var diff = await storage.Engine.DiffStatAsync(repo, from, to, cancellationToken).ConfigureAwait(false);
            var count = diff.ChangedPaths.Count(path => GameInsights.MatchesHint(path, hints));
            _modsChanged[key] = count;
            return count;
        }
        catch (GitOperationException)
        {
            return null;
        }
    }

    // Every file that changed between two points of the mods folder is a mod file; null when the comparison cannot be made.
    private async Task<int?> FilesChangedInModsAsync(StorageSnapshot storage, RepoRef repo, string from, string to, CancellationToken cancellationToken)
    {
        var key = $"{repo.GitDir}|{from}|{to}|all";
        if (_modsChanged.TryGetValue(key, out var known))
        {
            return known;
        }

        try
        {
            var count = (await storage.Engine.DiffStatAsync(repo, from, to, cancellationToken).ConfigureAwait(false)).ChangedPaths.Count();
            _modsChanged[key] = count;
            return count;
        }
        catch (GitOperationException)
        {
            return null;
        }
    }

    // The whole install as the point recorded it; null when its files cannot be read, which leaves the point without a size.
    private async Task<long?> InstallBytesAsync(RepoRef install, string commit, CancellationToken cancellationToken)
    {
        try
        {
            return await _services.Insights.TreeBytesAsync(install, commit, cancellationToken).ConfigureAwait(false);
        }
        catch (SavepointException)
        {
            return null;
        }
    }
}
