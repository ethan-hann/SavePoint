using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

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
                        foreach (var repo in repos)
                        {
                            var commits = await storage.Engine.ListCommitsOnlyOnAsync(repo, profile.BranchName, others, cancellationToken).ConfigureAwait(false);
                            var points = commits.Where(commit => commit.CaptureId is not null).ToList();
                            lost.UnionWith(points.Select(point => point.CaptureId!));
                            losses.Add(new FolderPointLoss(repo.Role, points.Count, null));
                        }

                        var blocker = BlockerOf(game, profile, repos);
                        return new ProfileDeletePreview(profile.Id, lost.Count, losses, blocker is null ? null : ErrorInfo.From(blocker));
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

                    await RemoveBranchesAsync(storage, repos, profile.BranchName).ConfigureAwait(false);
                    await _services.Catalog.DeleteProfileAsync(profile.Id).ConfigureAwait(false);
                    return Unit.Value;
                }).ConfigureAwait(false);

            // The points only that profile held are still in the store until this runs (D17, D28).
            _optimization.Start(gameId, OptimizationMode.Reclaim, preemptible: true);
            return Unit.Value;
        });

    // Removes the branch from every folder or, when one folder cannot, puts the others back where they were: the points stay in the
    // store until the tidy-up after a finished delete, so a branch made again at its old tip is the same profile as before.
    private static async Task RemoveBranchesAsync(StorageSnapshot storage, IReadOnlyList<RepoRef> repos, string branch)
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
            foreach (var (repo, tip) in removed)
            {
                await RestoreBranchAsync(storage, repo, branch, tip).ConfigureAwait(false);
            }

            throw;
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
            // A branch nothing refers to costs nothing but a name.
        }
    }
}
