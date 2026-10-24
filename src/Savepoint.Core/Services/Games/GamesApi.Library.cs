using Savepoint.Core.Api;

namespace Savepoint.Core.Services.Games;

internal sealed partial class GamesApi
{
    public Task<OperationResult<LibraryScan>> ScanLibrariesAsync(CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(_services.Log, () => Task.FromResult(new LibraryScan([], false, false)), cancellationToken);

    public Task<OperationResult<LibraryAddResult>> AddLibraryGameAsync(LibraryGame game) =>
        ApiGuard.RunAsync(_services.Log, () => Task.FromResult(new LibraryAddResult(game.Key, game.Name, LibraryAddStatus.Refused)));
}
