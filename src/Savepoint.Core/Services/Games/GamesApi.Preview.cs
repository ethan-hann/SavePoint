using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Profiles;

namespace Savepoint.Core.Services.Games;

internal sealed partial class GamesApi
{
    public Task<OperationResult<CapturePreview>> GetCapturePreviewAsync(string gameId, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                var game = await RequireGameAsync(gameId, cancellationToken).ConfigureAwait(false);
                return await _services.Storage.ReadAsync(
                    async storage =>
                    {
                        var repos = GameFolders.Repos(_services, storage, game);
                        var branches = (await _services.Catalog.ListProfilesAsync(game.Id, cancellationToken).ConfigureAwait(false)).Select(profile => profile.BranchName).ToList();

                        // The folders are separate repositories, so they are listed side by side.
                        var previews = await Task.WhenAll(repos.Select(repo => PreviewFolderAsync(storage, game, repo, branches, cancellationToken))).ConfigureAwait(false);
                        return new CapturePreview(game.Id, [.. previews.Select(preview => preview.Folder)], previews.Sum(preview => preview.NewBytes));
                    },
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    private async Task<(FolderCapturePreview Folder, long NewBytes)> PreviewFolderAsync(
        StorageSnapshot storage,
        GameDefinition game,
        RepoRef repo,
        IReadOnlyCollection<string> profileBranches,
        CancellationToken cancellationToken)
    {
        var rules = game.IgnorePathsFor(repo.Role);
        IReadOnlyList<CaptureCandidate> candidates;
        long newBytes;
        try
        {
            candidates = await storage.Engine.ListCaptureCandidatesAsync(repo, rules, cancellationToken).ConfigureAwait(false);
            newBytes = await storage.Engine.EstimateNewBytesAsync(repo, candidates, rules, cancellationToken).ConfigureAwait(false);
        }
        catch (ConsentMarkerException e)
        {
            // This folder's files are not read, but the others are still previewed, and the wizard gives this one its own reason.
            // The game folder never starts empty, so it reports no reason, as it does when its files are counted.
            var markerBlock = repo.Role == RepoRole.Install ? StartEmptyBlock.None
                : profileBranches.Count == 0 ? StartEmptyBlock.FirstProfile
                : e.Args["reason"] is ConsentFailure.Missing ? StartEmptyBlock.NotConfirmed
                : StartEmptyBlock.Other;
            return (new FolderCapturePreview(repo.Role, repo.WorkTree, 0, 0, 0, false, markerBlock, FilesCounted: false), 0);
        }

        var mods = repo.Role switch
        {
            RepoRole.Install => candidates.Count(candidate => GameInsights.MatchesHint(candidate.Path, game.ModPathHints)),
            RepoRole.Mods => candidates.Count,
            _ => 0,
        };

        // Only a folder a new profile could start empty is asked, and only when some profile exists to hold its files.
        var blocked = repo.Role == RepoRole.Install ? StartEmptyBlock.None
            : profileBranches.Count == 0 ? StartEmptyBlock.FirstProfile
            : await StartEmptyRule.WhyNotAsync(storage.Engine, repo, profileBranches, cancellationToken).ConfigureAwait(false);
        var held = repo.Role != RepoRole.Install && blocked == StartEmptyBlock.None;
        return (new FolderCapturePreview(repo.Role, repo.WorkTree, candidates.Count, mods, candidates.Sum(candidate => candidate.Bytes), held, blocked), newBytes);
    }
}
