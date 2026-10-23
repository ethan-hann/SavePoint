using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Services.History;

namespace Savepoint.Core.Services.Profiles;

internal sealed partial class ProfilesApi
{
    // The root point of a duplicate's saves, which has no files and no capture id: it is no one's save.
    private const string _startedMessage = "Profile started";

    public Task<OperationResult<Profile>> UpdateAsync(string gameId, string profileId, ProfileChanges changes) =>
        ApiGuard.RunAsync(_services.Log, async () =>
        {
            await RequireGameAsync(gameId).ConfigureAwait(false);
            await RequireProfileAsync(gameId, profileId).ConfigureAwait(false);

            // Only the edited fields are replaced, under the catalog's own lock, so a launch time written meanwhile survives.
            return await _services.Catalog.UpdateProfileAsync(
                profileId,
                profile => profile with { Name = changes.Name, Notes = changes.Notes, VersionOverride = changes.VersionOverride }).ConfigureAwait(false)
                ?? throw new ProfileNotFoundException(gameId, profileId);
        });

    public Task<OperationResult<Profile>> DuplicateAsync(string gameId, string sourceProfileId, string name) =>
        ApiGuard.RunAsync(_services.Log, async () =>
        {
            var game = await RequireGameAsync(gameId).ConfigureAwait(false);
            return await _services.RunForGameAsync(
                gameId,
                async (storage, _) =>
                {
                    var source = await RequireProfileAsync(gameId, sourceProfileId).ConfigureAwait(false);
                    var copy = CatalogValidator.RequireValid((await NewProfileAsync(gameId, name).ConfigureAwait(false)) with
                    {
                        Notes = source.Notes,
                        VersionOverride = source.VersionOverride,
                    });
                    var repos = GameFolders.Repos(_services, storage, game);
                    var install = repos.Single(repo => repo.Role == RepoRole.Install);

                    // The install folder continues from where the source is; the saves start over, so the copy begins with none of them.
                    var made = new List<RepoRef>();
                    try
                    {
                        await storage.Engine.CreateBranchAsync(install, copy.BranchName, source.BranchName).ConfigureAwait(false);
                        made.Add(install);
                        foreach (var saves in repos.Where(repo => repo.Role == RepoRole.External))
                        {
                            await storage.Engine.CreateEmptyBranchAsync(saves, copy.BranchName, _startedMessage).ConfigureAwait(false);
                            made.Add(saves);
                        }

                        return await _services.Catalog.SaveProfileAsync(copy).ConfigureAwait(false);
                    }
                    catch
                    {
                        // A branch is taken back from every folder that got one, so no half of the copy stays behind.
                        foreach (var repo in made)
                        {
                            await TakeBackBranchAsync(storage, repo, copy.BranchName).ConfigureAwait(false);
                        }

                        throw;
                    }
                }).ConfigureAwait(false);
        });

    public Task<OperationResult<Profile>> BringBackAsync(string gameId, HistoryPointId snapshotId, string name) =>
        ApiGuard.RunAsync(_services.Log, async () =>
        {
            var game = await RequireGameAsync(gameId).ConfigureAwait(false);
            return await _services.RunForGameAsync(
                gameId,
                async (storage, _) =>
                {
                    // The name is judged before anything is read from git, so a refused name costs nothing.
                    var fresh = CatalogValidator.RequireValid(await NewProfileAsync(gameId, name).ConfigureAwait(false));
                    var profiles = await _services.Catalog.ListProfilesAsync(gameId).ConfigureAwait(false);
                    var repos = GameFolders.Repos(_services, storage, game);
                    var state = await RemovedSnapshots.ReadStateAsync(storage, repos, profiles, CancellationToken.None).ConfigureAwait(false);
                    var group = state.Groups.FirstOrDefault(candidate => candidate.CaptureId == snapshotId.Value)
                        ?? throw new RemovedSnapshotNotFoundException(snapshotId.Value);
                    var origin = profiles.FirstOrDefault(profile => profile.BranchName == group.Lead.Branch);

                    // Each folder starts at the commit the snapshot recorded there, or at the latest one before it in the history of the profile it came from.
                    // A saves folder with none is left without a branch, as for any profile that never saved it; an install folder with none could not start.
                    var starts = new List<(RepoRef Repo, string Commit)>();
                    foreach (var repo in repos)
                    {
                        if (await RemovedSnapshots.CommitInAsync(storage, repo, state, group, null, CancellationToken.None).ConfigureAwait(false) is { } commit)
                        {
                            starts.Add((repo, commit.Id));
                        }
                        else if (repo.Role == RepoRole.Install)
                        {
                            throw new RemovedSnapshotNotRestorableException(snapshotId.Value);
                        }
                    }

                    var copy = fresh with { VersionOverride = origin?.VersionOverride };
                    var made = new List<RepoRef>();
                    try
                    {
                        foreach (var (repo, commit) in starts)
                        {
                            await storage.Engine.CreateBranchAsync(repo, copy.BranchName, commit).ConfigureAwait(false);
                            made.Add(repo);
                        }

                        var saved = await _services.Catalog.SaveProfileAsync(copy).ConfigureAwait(false);
                        _services.Log.Information($"Snapshot brought back: '{group.Lead.Commit.Subject}' ({snapshotId.Value}) of {gameId}, removed from {(origin is null ? group.Lead.Branch : origin.Name)}, is now the history of the new profile {copy.Name} ({copy.Id}); " + string.Join(", ", starts.Select(start => $"{GameFolders.LogName(start.Repo.Role)} branch starts at {start.Commit}")) + ". The profile it came from was not touched.");
                        return saved;
                    }
                    catch
                    {
                        // A branch is taken back from every folder that got one, so no half of the profile stays behind.
                        foreach (var repo in made)
                        {
                            await TakeBackBranchAsync(storage, repo, copy.BranchName).ConfigureAwait(false);
                        }

                        throw;
                    }
                }).ConfigureAwait(false);
        });

    public Task<OperationResult<ProfileDeletePreview>> GetDeletePreviewAsync(string gameId, string profileId, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                var game = await RequireGameAsync(gameId, cancellationToken).ConfigureAwait(false);
                var profile = await RequireProfileAsync(gameId, profileId, cancellationToken).ConfigureAwait(false);
                var others = (await _services.Catalog.ListProfilesAsync(gameId, cancellationToken).ConfigureAwait(false))
                    .Where(other => other.Id != profile.Id)
                    .Select(other => other.BranchName)
                    .ToList();
                return await _services.Storage.ReadAsync(
                    async storage =>
                    {
                        var repos = GameFolders.ReposForGitDirOnly(_services, storage, game);
                        var losses = new List<FolderPointLoss>();
                        var lost = new HashSet<string>(StringComparer.Ordinal);
                        var held = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var repo in repos)
                        {
                            // A commit a record of a removed snapshot names comes back to Recently removed after the delete; the rest is lost.
                            var onlyHere = (await storage.Engine.ListCommitsOnlyOnAsync(repo, profile.BranchName, others, cancellationToken).ConfigureAwait(false))
                                .Where(commit => commit.CaptureId is not null)
                                .ToList();
                            var recorded = (await storage.Engine.ListRemovedAsync(repo, cancellationToken).ConfigureAwait(false)).Select(removed => removed.Commit.Id).ToHashSet(StringComparer.Ordinal);
                            var forGood = onlyHere.Where(commit => !recorded.Contains(commit.Id)).ToList();
                            held.UnionWith(onlyHere.Select(commit => commit.CaptureId!));
                            lost.UnionWith(forGood.Select(point => point.CaptureId!));
                            losses.Add(new FolderPointLoss(repo.Role, forGood.Count, null));
                        }

                        // Each folder is judged on its own commit: a snapshot with one commit that no record names is lost for good, whatever
                        // the other folder's commit does, and only one whose every commit here is recorded goes back to Recently removed.
                        var blocker = BlockerOf(game, profile, repos);
                        return new ProfileDeletePreview(profile.Id, lost.Count, losses, blocker is null ? null : ErrorInfo.From(blocker), held.Count(captureId => !lost.Contains(captureId)));
                    },
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    public Task<OperationResult<Unit>> DeleteAsync(string gameId, string profileId) =>
        ApiGuard.RunAsync(_services.Log, async () =>
        {
            var game = await RequireGameAsync(gameId).ConfigureAwait(false);
            await _services.RunForGameAsync(
                gameId,
                async (storage, _) =>
                {
                    var profile = await RequireProfileAsync(gameId, profileId).ConfigureAwait(false);
                    var repos = GameFolders.Repos(_services, storage, game);

                    // Both folders are judged before either is touched, so a refused delete changes nothing (D13.9).
                    if (BlockerOf(game, profile, repos) is { } blocker)
                    {
                        throw blocker;
                    }

                    var removed = await RemoveBranchesAsync(storage, repos, profile.BranchName).ConfigureAwait(false);

                    // The row goes last, and a row that stays puts the branches back: a profile with a row and no branches says "nothing saved yet".
                    try
                    {
                        await _services.Catalog.DeleteProfileAsync(profile.Id).ConfigureAwait(false);
                    }
                    catch
                    {
                        await RestoreBranchesAsync(storage, removed, profile.BranchName).ConfigureAwait(false);
                        throw;
                    }

                    var tips = removed.Count == 0 ? "it had no points" : string.Join(", ", removed.Select(branch => $"{GameFolders.LogName(branch.Repo.Role)} branch {profile.BranchName} was at {branch.Tip}"));
                    _services.Log.Information($"Profile deleted: {profile.Name} ({profile.Id}) of {gameId}; {tips}. The points only it held stay in the store until the tidy-up that follows.");
                    return Unit.Value;
                }).ConfigureAwait(false);

            // The points only that profile held are still in the store until this runs (D17, D28).
            _optimization.Start(gameId, OptimizationMode.Reclaim, preemptible: true);
            return Unit.Value;
        });

    // Removes the branch from every folder or, when one folder cannot, puts the others back where they were: the points stay in the
    // store until the tidy-up after a finished delete, so a branch made again at its old tip is the same profile as before.
    private static async Task<IReadOnlyList<(RepoRef Repo, string Tip)>> RemoveBranchesAsync(StorageSnapshot storage, IReadOnlyList<RepoRef> repos, string branch)
    {
        var removed = new List<(RepoRef Repo, string Tip)>();
        try
        {
            foreach (var repo in repos)
            {
                var tip = await storage.Engine.GetBranchTipAsync(repo, branch).ConfigureAwait(false);
                await storage.Engine.DeleteBranchAsync(repo, branch).ConfigureAwait(false);
                if (tip is not null)
                {
                    removed.Add((repo, tip));
                }
            }
        }
        catch
        {
            await RestoreBranchesAsync(storage, removed, branch).ConfigureAwait(false);
            throw;
        }

        return removed;
    }

    private static async Task RestoreBranchesAsync(StorageSnapshot storage, IEnumerable<(RepoRef Repo, string Tip)> removed, string branch)
    {
        foreach (var (repo, tip) in removed)
        {
            await RestoreBranchAsync(storage, repo, branch, tip).ConfigureAwait(false);
        }
    }

    // Best effort, like the take-back: the failure being reported is the one worth showing.
    private static async Task RestoreBranchAsync(StorageSnapshot storage, RepoRef repo, string branch, string tip)
    {
        try
        {
            await storage.Engine.CreateBranchAsync(repo, branch, tip).ConfigureAwait(false);
        }
        catch (Exception e) when (e is SavepointException or IOException)
        {
            // The branch stays missing here; deleting the profile again finishes the job.
        }
    }

    // Why a delete would fail: the game is being played, or a folder is on the profile's branch. Null when it can go ahead.
    private SavepointException? BlockerOf(GameDefinition game, Profile profile, IEnumerable<RepoRef> repos)
    {
        if (_services.Sessions.IsGameRunning(game.Id))
        {
            return new GameRunningException(game.Id, game.Name);
        }

        return repos.Any(repo => BranchCheckedOut(repo, profile.BranchName)) ? new ProfileActiveException(profile.Id) : null;
    }

    private static bool BranchCheckedOut(RepoRef repo, string branch)
    {
        try
        {
            return string.Equals(GitDirFiles.ReadHead(repo.GitDir).Branch, branch, StringComparison.Ordinal);
        }
        catch (GitOperationException)
        {
            return false;
        }
    }

    // Best effort: the failure being reported is the one worth showing.
    private static async Task TakeBackBranchAsync(StorageSnapshot storage, RepoRef repo, string branch)
    {
        try
        {
            await storage.Engine.DeleteBranchAsync(repo, branch).ConfigureAwait(false);
        }
        catch (Exception e) when (e is SavepointException or IOException)
        {
            // A branch no profile names hides nothing, and Free up space removes it with the removed snapshots it holds.
        }
    }
}
