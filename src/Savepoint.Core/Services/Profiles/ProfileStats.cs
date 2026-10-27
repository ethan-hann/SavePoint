using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Services.History;

namespace Savepoint.Core.Services.Profiles;

/// <summary>Builds the figures on a profile row: whether it is active, what is unsaved, how many snapshots and mod files it holds, and its version.</summary>
internal static class ProfileStats
{
    // What one folder looks like right now; unknown when it cannot be read (the folder moved, its marker is gone).
    private sealed record FolderState(bool Known, string? Branch, bool Detached, bool Clean)
    {
        public static FolderState Unknown { get; } = new(false, null, false, true);

        public bool IsOn(string branch) => Known && !Detached && Branch == branch;
    }

    public static async Task<IReadOnlyList<ProfileRowStats>> ReadAsync(
        BackendServices services,
        StorageSnapshot storage,
        GameDefinition game,
        IReadOnlyList<Profile> profiles,
        CancellationToken cancellationToken)
    {
        var repos = GameFolders.ReposForGitDirOnly(services, storage, game);
        var install = repos.Single(repo => repo.Role == RepoRole.Install);
        var external = repos.FirstOrDefault(repo => repo.Role == RepoRole.External);

        // One status per folder serves every row.
        var installRead = StateOfAsync(storage, install, cancellationToken);
        var externalRead = external is null ? null : StateOfAsync(storage, external, cancellationToken);
        var installState = await installRead.ConfigureAwait(false);
        var externalState = externalRead is null ? null : await externalRead.ConfigureAwait(false);
        var running = services.Sessions.RunningProfileId(game.Id);
        var snapshots = await HistoryReader.CountByOwnerAsync(storage, repos, profiles, cancellationToken).ConfigureAwait(false);

        var rows = new Task<ProfileRowStats>[profiles.Count];
        for (var i = 0; i < profiles.Count; i++)
        {
            rows[i] = RowAsync(services, storage, game, profiles[i], install, external, installState, externalState, snapshots.GetValueOrDefault(profiles[i].Id), running, cancellationToken);
        }

        return await Task.WhenAll(rows).ConfigureAwait(false);
    }

    private static async Task<ProfileRowStats> RowAsync(
        BackendServices services,
        StorageSnapshot storage,
        GameDefinition game,
        Profile profile,
        RepoRef install,
        RepoRef? external,
        FolderState installState,
        FolderState? externalState,
        int snapshotCount,
        string? running,
        CancellationToken cancellationToken)
    {
        var branch = profile.BranchName;
        var ownsInstall = installState.IsOn(branch);
        var ownsExternal = externalState is null || externalState.IsOn(branch);

        // A saves folder left on an older point has no branch of its own; its owner is the profile the install folder is on.
        var detachedOwner = externalState is { Known: true, Detached: true } && installState.IsOn(branch);
        var unsaved = new List<RepoRole>();
        if (ownsInstall && !installState.Clean)
        {
            unsaved.Add(RepoRole.Install);
        }

        if (externalState is { Known: true, Clean: false } && (externalState.IsOn(branch) || detachedOwner))
        {
            unsaved.Add(RepoRole.External);
        }

        var active = ownsInstall && installState.Clean && ownsExternal && (externalState?.Clean ?? true);

        var tip = await storage.Engine.GetBranchTipAsync(install, branch, cancellationToken).ConfigureAwait(false);
        var snapshotNow = await SnapshotNowOfAsync(storage, branch, tip is not null, installState, external, externalState, cancellationToken).ConfigureAwait(false);
        int? modFiles = null;
        string? version = null;
        if (tip is not null)
        {
            modFiles = await services.Insights.ModFileCountAsync(install, tip, game.ModPathHints, cancellationToken).ConfigureAwait(false);
            version = await services.Insights.DetectedVersionAsync(install, tip, game.Launch, cancellationToken).ConfigureAwait(false);
        }

        return new ProfileRowStats(
            profile.Id,
            active,
            unsaved,
            snapshotCount,
            modFiles,
            version,
            profile.VersionOverride,
            profile.LastLaunchedAt,
            string.Equals(running, profile.Id, StringComparison.Ordinal),
            snapshotNow);
    }

    // A snapshot copies what the folders hold, so it fits the loaded profile and a profile that has no snapshot yet (whose first one is that), and nothing else.
    private static async Task<SnapshotNowAvailability> SnapshotNowOfAsync(
        StorageSnapshot storage,
        string branch,
        bool installHasBranch,
        FolderState installState,
        RepoRef? external,
        FolderState? externalState,
        CancellationToken cancellationToken)
    {
        var externalHasBranch = external is not null && await storage.Engine.GetBranchTipAsync(external, branch, cancellationToken).ConfigureAwait(false) is not null;
        var fits = SnapshotNowRule.Fits(installHasBranch, installState.Detached, installState.Branch, branch)
            && (externalState is null || SnapshotNowRule.Fits(externalHasBranch, externalState.Detached, externalState.Branch, branch));

        // A folder with no branch of the profile takes its first snapshot, and that puts the folder on the profile, so any such folder asks first.
        var everyFolderHasBranch = installHasBranch && (external is null || externalHasBranch);
        return !fits ? SnapshotNowAvailability.NotLoaded : everyFolderHasBranch ? SnapshotNowAvailability.Available : SnapshotNowAvailability.FirstSnapshot;
    }

    private static async Task<FolderState> StateOfAsync(StorageSnapshot storage, RepoRef repo, CancellationToken cancellationToken)
    {
        try
        {
            var status = await storage.Engine.GetStatusAsync(repo, cancellationToken, waitForNote: false).ConfigureAwait(false);
            return new FolderState(true, status.Branch, status.IsDetached, status.IsClean);
        }
        catch (Exception e) when (e is SavepointException or IOException)
        {
            return FolderState.Unknown;
        }
    }
}
