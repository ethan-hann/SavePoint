using System.Text.Json;
using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Services.Games;

/// <summary>The real <see cref="IGamesApi"/>: defines, changes, inspects and deletes games over the catalog and the git engine.</summary>
internal sealed partial class GamesApi : IGamesApi
{
    private readonly BackendServices _services;

    public GamesApi(BackendServices services)
    {
        _services = services;
    }

    public IReadOnlyList<GameTemplate> Templates => GameTemplates.All;

    public Task<OperationResult<IReadOnlyList<GameDefinition>>> ListAsync(CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(() => _services.Catalog.ListGamesAsync(cancellationToken), cancellationToken);

    public Task<OperationResult<IReadOnlyList<FolderStatus>>> GetFolderStatusAsync(string gameId, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync<IReadOnlyList<FolderStatus>>(
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
        ApiGuard.RunAsync(() => Task.FromResult(_services.PathTokens.Resolve(rawPath)), cancellationToken);

    public Task<OperationResult<IReadOnlyList<string>>> ListExecutablesAsync(string installPath, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            async () => await _services.Executables.ListAsync(_services.PathTokens.Resolve(installPath, "install.path"), cancellationToken).ConfigureAwait(false),
            cancellationToken);

    public Task<OperationResult<GameDefinition>> ImportDefinitionAsync(string filePath, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
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

    public Task<OperationResult<GameDefinition>> ResolveOrphanAsync(GameDefinition definition, OrphanResolution resolution) =>
        ApiGuard.NotBuilt<GameDefinition>();

    public Task<OperationResult<GameDefinition>> UpdateAsync(GameDefinition definition) => ApiGuard.NotBuilt<GameDefinition>();

    public Task<OperationResult<Unit>> DeleteAsync(string gameId) => ApiGuard.NotBuilt<Unit>();

    public Task<OperationResult<CapturePreview>> GetCapturePreviewAsync(string gameId, CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<CapturePreview>();

    public Task<OperationResult<JobStarted>> OptimizeStorageAsync(string gameId) => ApiGuard.NotBuilt<JobStarted>();

    // An id the catalog does not know is a caller mistake, reported as an unknown error with the id in its detail.
    private async Task<GameDefinition> RequireGameAsync(string gameId, CancellationToken cancellationToken) =>
        await _services.Catalog.GetGameAsync(gameId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"There is no game '{gameId}'.");

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
