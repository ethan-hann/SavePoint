using System.Text.Json;
using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Launch;
using Savepoint.Core.Library;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Services.Games;

/// <summary>The real <see cref="IGamesApi"/>: defines, changes, inspects and deletes games over the catalog and the git engine.</summary>
internal sealed partial class GamesApi : IGamesApi
{
    private readonly BackendServices _services;
    private readonly OptimizationService _optimization;

    private readonly LibrarySources _library;

    public GamesApi(BackendServices services, OptimizationService optimization, LibrarySources library)
    {
        _services = services;
        _optimization = optimization;
        _library = library;
    }

    public IReadOnlyList<GameTemplate> Templates => _library.Templates;

    public Task<OperationResult<IReadOnlyList<GameDefinition>>> ListAsync(CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(_services.Log, () => _services.Catalog.ListGamesAsync(cancellationToken), cancellationToken);

    public Task<OperationResult<IReadOnlyList<FolderStatus>>> GetFolderStatusAsync(string gameId, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync<IReadOnlyList<FolderStatus>>(
            _services.Log,
            async () =>
            {
                var game = await RequireGameAsync(gameId, cancellationToken).ConfigureAwait(false);
                return await _services.Storage.ReadAsync(
                    storage => Task.FromResult<IReadOnlyList<FolderStatus>>(
                    [
                        .. GameFolders.ResolveLoosely(_services.PathTokens, game).Select(folder => StatusOf(storage, game, folder)),
                    ]),
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    public Task<OperationResult<GameStorageUsage>> GetStorageUsageAsync(string gameId, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                var game = await RequireGameAsync(gameId, cancellationToken).ConfigureAwait(false);
                return await _services.Storage.ReadAsync(
                    async storage =>
                    {
                        var bytes = await DirectorySize.GetBytesAsync(storage.GameDirectory(game.Id), cancellationToken).ConfigureAwait(false);
                        return new GameStorageUsage(
                            game.Id,
                            bytes,
                            _services.Disk.GetTotalBytes(storage.Root) ?? 0,
                            _services.Disk.GetFreeBytes(storage.Root) ?? 0);
                    },
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    public Task<OperationResult<string>> ResolvePathAsync(string rawPath, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(_services.Log, () => Task.FromResult(_services.PathTokens.Resolve(rawPath)), cancellationToken);

    public Task<OperationResult<IReadOnlyList<string>>> ListExecutablesAsync(string installPath, string gameName, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () => await _services.Executables.ListAsync(_services.PathTokens.Resolve(installPath, "install.path"), cancellationToken, gameName).ConfigureAwait(false),
            cancellationToken);

    public Task<OperationResult<string>> SuggestProcessNameAsync(string installPath, string gameName, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                var folder = _services.PathTokens.Resolve(installPath, "install.path");
                var programs = await _services.Executables.ListProgramsAsync(folder, cancellationToken).ConfigureAwait(false);
                return ProcessNameSuggester.Suggest(Path.GetFileName(folder.TrimEnd('\\', '/')), gameName, programs, folder) ?? string.Empty;
            },
            cancellationToken);

    public Task<OperationResult<GameDefinition>> ImportDefinitionAsync(string filePath, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
                GameDefinition? read;
                try
                {
                    read = JsonSerializer.Deserialize(bytes, CatalogJsonContext.Default.GameDefinition);
                }
                catch (JsonException e)
                {
                    throw new InvalidDataException($"'{filePath}' is not a game definition file.", e);
                }

                // A file written by hand may leave the id out; the editor gives an imported game its own id before it is saved.
                var definition = read ?? throw new InvalidDataException($"'{filePath}' is empty.");
                return CatalogValidator.RequireValid(string.IsNullOrWhiteSpace(definition.Id) ? definition with { Id = CatalogIds.NewGameId(definition.Name) } : definition);
            },
            cancellationToken);

    public Task<OperationResult<Unit>> ExportDefinitionAsync(string gameId, string filePath, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                var game = await RequireGameAsync(gameId, cancellationToken).ConfigureAwait(false);
                var text = JsonSerializer.Serialize(game, CatalogJsonContext.Default.GameDefinition) + "\n";
                var folder = Path.GetDirectoryName(Path.GetFullPath(filePath));
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                await File.WriteAllTextAsync(filePath, text, new System.Text.UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
                return Unit.Value;
            },
            cancellationToken);






    // An id the catalog does not know is answered as not found, so a game removed in the meantime reads as that.
    private async Task<GameDefinition> RequireGameAsync(string gameId, CancellationToken cancellationToken) =>
        await _services.Catalog.GetGameAsync(gameId, cancellationToken).ConfigureAwait(false)
        ?? throw new GameNotFoundException(gameId);

    private FolderStatus StatusOf(StorageSnapshot storage, GameDefinition game, GameFolder folder)
    {
        if (folder.Path is null)
        {
            return new FolderStatus(folder.Role, folder.Raw, null, false, new ConsentStatus(false, null));
        }

        var consent = storage.Engine.GetConsentStatus(GameFolders.Repo(storage, game.Id, folder));
        return new FolderStatus(folder.Role, folder.Raw, folder.Path, Directory.Exists(folder.Path), new ConsentStatus(consent.Confirmed, consent.ConfirmedAt));
    }
}
