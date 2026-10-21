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
    private readonly ConcurrentDictionary<string, Change> _changes = new(StringComparer.Ordinal);

    public HistoryReader(BackendServices services)
    {
        _services = services;
    }

    // One commit of a capture event, in the lineage of the profile that owns it.
    private sealed record Entry(RepoRole Role, Profile Profile, CommitInfo Commit, string? Previous);

    private sealed record Change(int ModFilesChanged, long Bytes);

    private sealed class RawPoint(string captureId)
    {
        public string CaptureId { get; } = captureId;

        public Entry? Install { get; set; }

        public Entry? External { get; set; }

        public Profile Owner => (Install ?? External)!.Profile;

        public DateTimeOffset At => (Install ?? External)!.Commit.Date;

        public string Name => (Install ?? External)!.Commit.Subject;
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
        var external = repos.FirstOrDefault(repo => repo.Role == RepoRole.External);

        var lineages = await ReadLineagesAsync(storage, repos, profiles, cancellationToken).ConfigureAwait(false);
        var raw = Group(repos, profiles, lineages);
        var heads = await ReadHeadsAsync(storage, repos, cancellationToken).ConfigureAwait(false);

        var wanted = raw.Where(point => profileId is null || point.Owner.Id == profileId).ToList();
        var installAt = wanted.ToDictionary(point => point.CaptureId, point => InstallCommitFor(point, lineages, install));
        var points = new HistoryPoint[wanted.Count];
        using var slots = new SemaphoreSlim(_parallelism);
        await Task.WhenAll(wanted.Select(async (point, index) =>
        {
            await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                points[index] = await DescribeAsync(storage, game, install, external, point, installAt[point.CaptureId], heads, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                slots.Release();
            }
        })).ConfigureAwait(false);

        return new GameHistory(game.Id, profileId, [.. points.OrderByDescending(point => point.CapturedAt)]);
    }

    // Each profile's points in each folder, newest first; every (folder, profile) pair is one git call.
    private static async Task<Dictionary<(RepoRole, string), IReadOnlyList<CommitInfo>>> ReadLineagesAsync(
        StorageSnapshot storage,
        IReadOnlyList<RepoRef> repos,
        IReadOnlyList<Profile> profiles,
        CancellationToken cancellationToken)
    {
        var reads = repos.SelectMany(repo => profiles.Select(async profile =>
        {
            var commits = await storage.Engine.ListCommitsAsync(repo, profile.BranchName, null, cancellationToken).ConfigureAwait(false);
            return (Key: (repo.Role, profile.Id), Commits: commits);
        })).ToList();
        return (await Task.WhenAll(reads).ConfigureAwait(false)).ToDictionary(read => read.Key, read => read.Commits);
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
                    if (repo.Role == RepoRole.Install)
                    {
                        point.Install ??= entry;
                    }
                    else
                    {
                        point.External ??= entry;
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

    // The install commit that describes the point: its own, or for a save-only point the profile's install point current when it was made.
    private static string? InstallCommitFor(
        RawPoint point,
        Dictionary<(RepoRole, string), IReadOnlyList<CommitInfo>> lineages,
        RepoRef install)
    {
        if (point.Install is { } own)
        {
            return own.Commit.Id;
        }

        return lineages[(install.Role, point.Owner.Id)]
            .Where(commit => commit.CaptureId is not null && commit.Date <= point.At)
            .OrderByDescending(commit => commit.Date)
            .Select(commit => commit.Id)
            .FirstOrDefault();
    }

    private async Task<HistoryPoint> DescribeAsync(
        StorageSnapshot storage,
        GameDefinition game,
        RepoRef install,
        RepoRef? external,
        RawPoint point,
        string? installCommit,
        Dictionary<RepoRole, string?> heads,
        CancellationToken cancellationToken)
    {
        var kind = (point.Install, point.External) switch
        {
            (not null, not null) => HistoryPointKind.VersionModsAndSave,
            (not null, null) => HistoryPointKind.VersionAndMods,
            _ => HistoryPointKind.SaveOnly,
        };

        var state = await InstallStateAsync(storage, game, install, point.Owner, installCommit, cancellationToken).ConfigureAwait(false);
        var installChange = point.Install is { Previous: { } before } own
            ? await ChangeAsync(storage, install, before, own.Commit.Id, game.ModPathHints, cancellationToken).ConfigureAwait(false)
            : null;
        var savesChange = point.External is { Previous: { } previousSave } saves && external is not null
            ? await ChangeAsync(storage, external, previousSave, saves.Commit.Id, [], cancellationToken).ConfigureAwait(false)
            : null;

        // The mod-file count is only meaningful where the game names mod folders; a first point has nothing to compare with.
        int? modsChanged = installChange is not null && game.ModPathHints.Count > 0 ? installChange.ModFilesChanged : null;
        long? bytes = installChange is null && savesChange is null ? null : (installChange?.Bytes ?? 0) + (savesChange?.Bytes ?? 0);

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
            InUse: point.Install is { } held && heads.GetValueOrDefault(RepoRole.Install) == held.Commit.Id,
            LoadedNow: point.External is { } loaded && heads.GetValueOrDefault(RepoRole.External) == loaded.Commit.Id);
    }

    private async Task<InstallState> InstallStateAsync(
        StorageSnapshot storage,
        GameDefinition game,
        RepoRef install,
        Profile owner,
        string? commit,
        CancellationToken cancellationToken)
    {
        if (commit is null)
        {
            return new InstallState(owner.VersionOverride, null);
        }

        try
        {
            var mods = await _services.Insights.ModFileCountAsync(install, commit, game.ModPathHints, cancellationToken).ConfigureAwait(false);
            var version = owner.VersionOverride ?? await _services.Insights.DetectedVersionAsync(install, commit, game.Launch, cancellationToken).ConfigureAwait(false);
            return new InstallState(version, mods);
        }
        catch (SavepointException)
        {
            // A point whose files cannot be read is still a point; what it held is left unsaid.
            return new InstallState(owner.VersionOverride, null);
        }
    }

    // What changed between two points of one folder: the mod files among them and the bytes that are new.
    private async Task<Change?> ChangeAsync(
        StorageSnapshot storage,
        RepoRef repo,
        string from,
        string to,
        IReadOnlyList<string> hints,
        CancellationToken cancellationToken)
    {
        var key = $"{repo.GitDir}|{from}|{to}|{string.Join('\u001f', hints)}";
        if (_changes.TryGetValue(key, out var known))
        {
            return known;
        }

        try
        {
            var diff = await storage.Engine.DiffStatAsync(repo, from, to, cancellationToken).ConfigureAwait(false);
            var change = new Change(diff.ChangedPaths.Count(path => GameInsights.MatchesHint(path, hints)), diff.BytesAtTarget);
            _changes[key] = change;
            return change;
        }
        catch (GitOperationException)
        {
            return null;
        }
    }
}
