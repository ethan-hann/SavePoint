using Savepoint.Core.Api;
using Savepoint.Core.Errors;
using Savepoint.Core.Library;

namespace Savepoint.Core.Services.Games;

internal sealed partial class GamesApi
{
    public Task<OperationResult<LibraryScan>> ScanLibrariesAsync(CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                var games = await _services.Catalog.ListGamesAsync(cancellationToken).ConfigureAwait(false);
                return await _services.Storage.ReadAsync(
                    storage => Task.Run(
                        () => LibraryScanner.Scan(_library, _services.Platform, _services.PathTokens, storage.PathSafety, games, cancellationToken),
                        cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    public Task<OperationResult<LibraryAddResult>> AddLibraryGameAsync(LibraryGame game) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                ArgumentNullException.ThrowIfNull(game);
                var games = await _services.Catalog.ListGamesAsync().ConfigureAwait(false);
                var already = await _services.Storage.ReadAsync(
                    storage => Task.FromResult(LibraryScanner.FindAdded(game.Draft.Install.Path, game.Draft.SteamAppId, games, _services.PathTokens, storage.PathSafety))).ConfigureAwait(false);
                if (already is not null)
                {
                    return new LibraryAddResult(game.Key, game.Name, LibraryAddStatus.AlreadyAdded, already);
                }

                // A game makes its saves folder the first time it is played, and a folder that is not there cannot be tracked.
                var draft = game.Draft;
                string? leftOut = null;
                if (draft.External is { } saves && SavesFolderMissing(saves.Path) is { } missing)
                {
                    draft = draft with { External = null };
                    leftOut = missing;
                }

                var added = await AddAsync(draft).ConfigureAwait(false);
                if (!added.Ok)
                {
                    return new LibraryAddResult(game.Key, game.Name, LibraryAddStatus.Refused, Error: added.Error);
                }

                return added.Value switch
                {
                    AddGameOutcome.Added done => new LibraryAddResult(game.Key, game.Name, LibraryAddStatus.Added, done.Game.Id, SavesFolderLeftOut: leftOut),
                    _ => new LibraryAddResult(game.Key, game.Name, LibraryAddStatus.HasOlderHistory),
                };
            });

    // The resolved path of a saves folder that does not exist; null when it exists or cannot be resolved (adding then reports why).
    private string? SavesFolderMissing(string raw)
    {
        try
        {
            var resolved = _services.PathTokens.Resolve(raw, "external.path");
            return Directory.Exists(resolved) ? null : resolved;
        }
        catch (ValidationException)
        {
            return null;
        }
    }
}
