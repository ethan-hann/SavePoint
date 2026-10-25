using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Launch;
using Savepoint.Core.Library;

namespace Savepoint.Core.Services.Games;

internal sealed partial class GamesApi
{
    public Task<OperationResult<LibraryScan>> ScanLibrariesAsync(CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                // The stores are read outside the storage gate, which only the "already added" checks need.
                var scanned = await Task.Run(() => LibraryScanner.Scan(_library, _services.Platform, _services.PathTokens, cancellationToken), cancellationToken).ConfigureAwait(false);
                var games = await _services.Catalog.ListGamesAsync(cancellationToken).ConfigureAwait(false);
                return await _services.Storage.ReadAsync(
                    storage => Task.FromResult(LibraryScanner.Annotate(scanned, games, _services.PathTokens, storage.PathSafety, cancellationToken)),
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
                var resolved = LibraryScanner.ResolveExisting(games, _services.PathTokens);
                var already = await _services.Storage.ReadAsync(
                    storage => Task.FromResult(LibraryScanner.FindAdded(game.InstallPath, game.SavesFolder, LibraryScanner.SteamIdOf(game), resolved, storage.PathSafety))).ConfigureAwait(false);
                if (already is not null)
                {
                    return new LibraryAddResult(game.Key, game.Name, LibraryAddStatus.AlreadyAdded, already);
                }

                // Only the folders the player was shown are added: the install folder, and the saves folder when the scan found it there
                // and it still is. A folder that appeared since, or a draft that names another one, is left out.
                var draft = game.Draft with { Install = new InstallLocation(game.InstallPath) };
                string? leftOut = null;
                if (draft.External is { } saves && !SavesFolderWasShownAndIsThere(game, saves.Path))
                {
                    draft = draft with { External = null };
                    leftOut = game.SavesFolder ?? SavesFolderPath(saves.Path);
                }

                draft = await WithProcessNameAsync(draft).ConfigureAwait(false);
                // Every rule of a scanned draft came from a template, none from the player.
                var added = await AddAsync(draft, draft.IgnorePaths).ConfigureAwait(false);
                if (!added.Ok)
                {
                    return new LibraryAddResult(game.Key, game.Name, LibraryAddStatus.Refused, Error: added.Error);
                }

                return added.Value switch
                {
                    AddGameOutcome.Added done => new LibraryAddResult(game.Key, game.Name, LibraryAddStatus.Added, done.Game.Id, SavesFolderLeftOut: leftOut, RulesLeftOff: done.LeftOff),
                    _ => new LibraryAddResult(game.Key, game.Name, LibraryAddStatus.HasOlderHistory),
                };
            });

    // A game that starts through a store link has no program of its own to follow, so the one that is most likely the game is named
    // and the session can end by itself.
    private async Task<GameDefinition> WithProcessNameAsync(GameDefinition draft)
    {
        if (draft.Launch.Method != LaunchMethod.Uri || !string.IsNullOrWhiteSpace(draft.Launch.ProcessNameForExitDetection))
        {
            return draft;
        }

        var folder = draft.Install.Path;
        var programs = await _services.Executables.ListProgramsAsync(folder).ConfigureAwait(false);
        var name = ProcessNameSuggester.Suggest(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)), draft.Name, programs, folder);
        return name is null ? draft : draft with { Launch = draft.Launch with { ProcessNameForExitDetection = name } };
    }

    // True when the scan showed this very saves folder as being there and it still is.
    private bool SavesFolderWasShownAndIsThere(LibraryGame game, string raw) =>
        game.SavesFolderExists
        && game.SavesFolder is { } shown
        && SavesFolderPath(raw) is { } now
        && string.Equals(now, shown, _services.Platform.PathComparison)
        && Directory.Exists(now);

    private string? SavesFolderPath(string raw)
    {
        try
        {
            return _services.PathTokens.Resolve(raw, "external.path");
        }
        catch (ValidationException)
        {
            return null;
        }
    }
}
