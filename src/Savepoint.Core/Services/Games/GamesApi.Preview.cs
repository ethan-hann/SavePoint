using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

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

                        // The folders are separate repositories, so they are listed side by side.
                        var previews = await Task.WhenAll(repos.Select(repo => PreviewFolderAsync(storage, game, repo, cancellationToken))).ConfigureAwait(false);
                        return new CapturePreview(game.Id, [.. previews.Select(preview => preview.Folder)], previews.Sum(preview => preview.NewBytes));
                    },
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    private async Task<(FolderCapturePreview Folder, long NewBytes)> PreviewFolderAsync(
        StorageSnapshot storage,
        GameDefinition game,
        RepoRef repo,
        CancellationToken cancellationToken)
    {
        var rules = game.IgnorePathsFor(repo.Role);
        var candidates = await storage.Engine.ListCaptureCandidatesAsync(repo, rules, cancellationToken).ConfigureAwait(false);
        var newBytes = await storage.Engine.EstimateNewBytesAsync(repo, candidates, rules, cancellationToken).ConfigureAwait(false);
        var mods = repo.Role == RepoRole.Install
            ? candidates.Count(candidate => GameInsights.MatchesHint(candidate.Path, game.ModPathHints))
            : 0;
        return (new FolderCapturePreview(repo.Role, repo.WorkTree, candidates.Count, mods, candidates.Sum(candidate => candidate.Bytes)), newBytes);
    }
}
