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
    public Task<OperationResult<LaunchPreview>> GetPreviewAsync(string gameId, LaunchTarget target, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            async () =>
            {
                ArgumentNullException.ThrowIfNull(target);
                var game = await RequireGameAsync(gameId, cancellationToken).ConfigureAwait(false);
                var profiles = await _services.Catalog.ListProfilesAsync(gameId, cancellationToken).ConfigureAwait(false);

                // A read: it holds the storage gate shared and takes no game lock, so a capture or a switch is never held up by it.
                return await _services.Storage.ReadAsync(storage => PreviewAsync(storage, game, profiles, target, cancellationToken), cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    private async Task<GameDefinition> RequireGameAsync(string gameId, CancellationToken cancellationToken = default) =>
        await _services.Catalog.GetGameAsync(gameId, cancellationToken).ConfigureAwait(false) ?? throw new GameNotFoundException(gameId);

    private async Task<LaunchPreview> PreviewAsync(
        StorageSnapshot storage,
        GameDefinition game,
        IReadOnlyList<Profile> profiles,
        LaunchTarget target,
        CancellationToken cancellationToken)
    {
        var repos = GameFolders.Repos(_services, storage, game);
        var resolved = await TargetResolver.ResolveAsync(storage, repos, profiles, target, cancellationToken).ConfigureAwait(false);
        var readings = await Task.WhenAll(resolved.Folders.Select(folder => FolderReading.ReadAsync(storage.Engine, folder, profiles, cancellationToken))).ConfigureAwait(false);
        var install = readings.Single(reading => reading.Target.Role == RepoRole.Install);
        var external = readings.FirstOrDefault(reading => reading.Target.Role == RepoRole.External);

        var states = await Task.WhenAll(readings.Select(reading => FolderStateAsync(storage, game, profiles, target, reading, cancellationToken))).ConfigureAwait(false);
        var currentOverride = install.Status.IsDetached ? null : profiles.FirstOrDefault(profile => profile.BranchName == install.Status.Branch)?.VersionOverride;
        var current = await InstallStateAsync(storage, game, install.Target.Repo, install.Status.HeadCommit, currentOverride, cancellationToken).ConfigureAwait(false);
        var after = install.Target.Leave
            ? current
            : await InstallStateAsync(storage, game, install.Target.Repo, install.Target.Commit, resolved.Profile.VersionOverride, cancellationToken).ConfigureAwait(false);

        return new LaunchPreview(
            game.Id,
            resolved.Profile.Id,
            target,
            states.Single(state => state.Role == RepoRole.Install),
            states.FirstOrDefault(state => state.Role == RepoRole.External),
            current,
            after,
            resolved.Saves,
            resolved.SelectedSave,
            ShowBanner: readings.All(reading => reading.Matches),
            OnlySaveDiffers: install.Matches && external is { Matches: false, Dirty: false },
            TargetIsOlderPoint: resolved.IsOlderPoint);
    }

    private async Task<FolderLaunchState> FolderStateAsync(
        StorageSnapshot storage,
        GameDefinition game,
        IReadOnlyList<Profile> profiles,
        LaunchTarget previewed,
        FolderReading reading,
        CancellationToken cancellationToken) =>
        new(
            reading.Target.Role,
            reading.Target.Repo.WorkTree,
            reading.Matches,
            reading.Dirty,
            reading.DirtyFileCount,
            reading.DirtyOwnerProfileId,
            reading.Matches || reading.Target.Leave ? null : await SwitchSummaryAsync(storage, game, reading, cancellationToken).ConfigureAwait(false),
            reading.DirtyNewFileCount,
            reading.UnfinishedSwitch,
            reading.UnfinishedSwitch ? await UnfinishedTargetAsync(storage, profiles, previewed, reading, cancellationToken).ConfigureAwait(false) : null);

    // Starting the previewed target finishes a switch that was heading there; one that was heading elsewhere is finished by that point's own target.
    private static async Task<LaunchTarget?> UnfinishedTargetAsync(
        StorageSnapshot storage,
        IReadOnlyList<Profile> profiles,
        LaunchTarget previewed,
        FolderReading reading,
        CancellationToken cancellationToken) =>
        reading.Pending
            ? previewed
            : await TargetResolver.TargetOfCommitAsync(storage, reading.Target.Repo, profiles, reading.Note!.ToCommit, cancellationToken).ConfigureAwait(false);

    // What the switch would write or remove, read from the two points alone.
    private async Task<SwitchSummary> SwitchSummaryAsync(StorageSnapshot storage, GameDefinition game, FolderReading reading, CancellationToken cancellationToken)
    {
        var repo = reading.Target.Repo;
        var target = reading.Target.Commit;

        // After a stopped switch HEAD may already be at the target, so the point the switch started from is the better "before".
        var from = reading.Pending && reading.Status.HeadCommit == target ? reading.Note!.FromCommit : reading.Status.HeadCommit;
        IReadOnlyList<string> changed;
        long bytes;
        if (from is null)
        {
            changed = await storage.Engine.ListTreeFilesAsync(repo, target, cancellationToken).ConfigureAwait(false);
            bytes = await _services.Insights.TreeBytesAsync(repo, target, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var diff = await storage.Engine.DiffStatAsync(repo, from, target, cancellationToken).ConfigureAwait(false);
            changed = diff.ChangedPaths;
            bytes = diff.BytesAtTarget;
        }

        var mods = repo.Role == RepoRole.Install ? changed.Count(path => GameInsights.MatchesHint(path, game.ModPathHints)) : 0;
        return new SwitchSummary(changed.Count, mods, bytes, null);
    }

    // The version and mod-file count a commit of the install folder holds; what cannot be read is left unsaid.
    private async Task<InstallState> InstallStateAsync(
        StorageSnapshot storage,
        GameDefinition game,
        RepoRef install,
        string? commit,
        string? versionOverride,
        CancellationToken cancellationToken)
    {
        if (commit is null)
        {
            return new InstallState(versionOverride, null);
        }

        try
        {
            var mods = await _services.Insights.ModFileCountAsync(install, commit, game.ModPathHints, cancellationToken).ConfigureAwait(false);
            var version = versionOverride ?? await _services.Insights.DetectedVersionAsync(install, commit, game.Launch, cancellationToken).ConfigureAwait(false);
            return new InstallState(version, mods);
        }
        catch (SavepointException)
        {
            return new InstallState(versionOverride, null);
        }
    }
}
