using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Launch.Orchestration;

// Where one folder is to be put: a commit, and the branch to attach to when that commit is the branch's tip.
internal sealed record FolderTarget(RepoRef Repo, string Commit, string? Branch)
{
    public RepoRole Role => Repo.Role;

    // A folder left on a commit that is not its branch's tip is on an older point (D14); it is checked out detached.
    public bool IsOlder => Branch is null;
}

// What a launch target comes to: the profile it belongs to, each folder's commit (install first), and the saves on offer.
internal sealed record ResolvedTarget(Profile Profile, IReadOnlyList<FolderTarget> Folders, IReadOnlyList<SaveEntry> Saves, HistoryPointId? SelectedSave)
{
    public bool IsOlderPoint => Folders.Any(folder => folder.IsOlder);

    public FolderTarget? For(RepoRole role) => Folders.FirstOrDefault(folder => folder.Role == role);
}

// Turns a launch target into commits (D15): a profile's tip, one of its saves, or the folders as one capture event left them.
internal static class TargetResolver
{
    public static async Task<ResolvedTarget> ResolveAsync(
        StorageSnapshot storage,
        IReadOnlyList<RepoRef> repos,
        IReadOnlyList<Profile> profiles,
        LaunchTarget target,
        CancellationToken cancellationToken)
    {
        var gameId = repos[0].GameId;
        return target switch
        {
            LaunchTarget.ProfileTip tip => await ResolveBranchAsync(storage, repos, RequireProfile(profiles, gameId, tip.ProfileId), null, cancellationToken).ConfigureAwait(false),
            LaunchTarget.Save save => await ResolveBranchAsync(storage, repos, RequireProfile(profiles, gameId, save.ProfileId), save.SaveId, cancellationToken).ConfigureAwait(false),
            LaunchTarget.Point point => await ResolvePointAsync(storage, repos, profiles, point.PointId, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown launch target."),
        };
    }

    private static Profile RequireProfile(IReadOnlyList<Profile> profiles, string gameId, string profileId) =>
        profiles.FirstOrDefault(profile => profile.Id == profileId) ?? throw new ProfileNotFoundException(gameId, profileId);

    // The profile's latest install, and its latest saves or the one save chosen.
    private static async Task<ResolvedTarget> ResolveBranchAsync(
        StorageSnapshot storage,
        IReadOnlyList<RepoRef> repos,
        Profile profile,
        HistoryPointId? chosenSave,
        CancellationToken cancellationToken)
    {
        var folders = new List<FolderTarget>();
        IReadOnlyList<CommitInfo> savesLineage = [];
        foreach (var repo in repos)
        {
            var lineage = await storage.Engine.ListCommitsAsync(repo, profile.BranchName, null, cancellationToken).ConfigureAwait(false);
            if (lineage.Count == 0)
            {
                throw new ProfileHasNoPointsException(profile.Id);
            }

            if (repo.Role == RepoRole.External)
            {
                savesLineage = lineage;
            }

            folders.Add(new FolderTarget(repo, lineage[0].Id, profile.BranchName));
        }

        var saves = SavesOf(savesLineage);
        if (chosenSave is not { } wanted)
        {
            return new ResolvedTarget(profile, folders, saves, saves.Count > 0 ? saves[0].Id : null);
        }

        // Only a save of this profile can be chosen; the newest one is the tip, so it stays attached to the branch.
        var chosen = savesLineage.FirstOrDefault(commit => commit.CaptureId == wanted.Value);
        if (chosen is null || repos.All(repo => repo.Role != RepoRole.External))
        {
            throw new PointNotFoundException(wanted.Value);
        }

        var index = folders.FindIndex(folder => folder.Role == RepoRole.External);
        folders[index] = folders[index] with { Commit = chosen.Id, Branch = chosen.Id == savesLineage[0].Id ? profile.BranchName : null };
        return new ResolvedTarget(profile, folders, saves, wanted);
    }

    // The save points of a saves folder's lineage, newest first: the commits that a capture event wrote.
    private static List<SaveEntry> SavesOf(IReadOnlyList<CommitInfo> lineage)
    {
        var saves = new List<SaveEntry>();
        foreach (var commit in lineage.Where(commit => commit.CaptureId is not null))
        {
            saves.Add(new SaveEntry(new HistoryPointId(commit.CaptureId!), commit.Subject, commit.Date, IsMostRecent: saves.Count == 0));
        }

        return saves;
    }

    // Each folder goes to its commit of that capture event; a folder the event did not touch goes to the owning profile's latest
    // commit at or before the event. The owner is the first profile, in catalog order, whose history holds the event (as in History).
    private static async Task<ResolvedTarget> ResolvePointAsync(
        StorageSnapshot storage,
        IReadOnlyList<RepoRef> repos,
        IReadOnlyList<Profile> profiles,
        HistoryPointId pointId,
        CancellationToken cancellationToken)
    {
        var reads = repos.SelectMany(repo => profiles.Select(async profile =>
        {
            var commits = await storage.Engine.ListCommitsAsync(repo, profile.BranchName, null, cancellationToken).ConfigureAwait(false);
            return (Key: (repo.Role, profile.Id), Commits: commits);
        })).ToList();
        var lineages = (await Task.WhenAll(reads).ConfigureAwait(false)).ToDictionary(read => read.Key, read => read.Commits);

        (Profile Profile, CommitInfo Commit)? Holder(RepoRole role)
        {
            foreach (var profile in profiles)
            {
                if (lineages[(role, profile.Id)].FirstOrDefault(commit => commit.CaptureId == pointId.Value) is { } commit)
                {
                    return (profile, commit);
                }
            }

            return null;
        }

        var installHolder = Holder(RepoRole.Install);
        var externalHolder = repos.Any(repo => repo.Role == RepoRole.External) ? Holder(RepoRole.External) : null;
        var first = installHolder ?? externalHolder ?? throw new PointNotFoundException(pointId.Value);
        var owner = first.Profile;
        var happenedAt = first.Commit.Date;

        var folders = new List<FolderTarget>();
        CommitInfo? savesCommit = null;
        foreach (var repo in repos)
        {
            var lineage = lineages[(repo.Role, owner.Id)];
            var held = repo.Role == RepoRole.Install ? installHolder : externalHolder;
            var commit = lineage.FirstOrDefault(candidate => candidate.CaptureId == pointId.Value)
                ?? held?.Commit
                ?? lineage.Where(candidate => candidate.Date <= happenedAt).OrderByDescending(candidate => candidate.Date).FirstOrDefault()
                ?? lineage.LastOrDefault()
                ?? throw new ProfileHasNoPointsException(owner.Id);
            if (repo.Role == RepoRole.External)
            {
                savesCommit = commit;
            }

            folders.Add(new FolderTarget(repo, commit.Id, lineage.Count > 0 && lineage[0].Id == commit.Id ? owner.BranchName : null));
        }

        var saves = SavesOf(repos.Any(repo => repo.Role == RepoRole.External) ? lineages[(RepoRole.External, owner.Id)] : []);
        HistoryPointId? selected = savesCommit?.CaptureId is { } id && saves.Any(save => save.Id.Value == id) ? new HistoryPointId(id) : null;
        return new ResolvedTarget(owner, folders, saves, selected);
    }
}
