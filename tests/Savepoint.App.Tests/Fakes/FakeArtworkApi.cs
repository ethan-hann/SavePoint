using Savepoint.Core.Api;
using Savepoint.Core.Artwork;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Fakes;

// Artwork held in memory. It follows the real Api's rules for the user's own image (a whole PNG, JPEG or WebP, 25 MB at most) and
// for look again, without a Steam folder or a network: a test says what Steam would find with ScriptSteamFinds, and a test that
// wants the artwork to change on its own uses Script, which raises the change event as the real lookup does.
public sealed class FakeArtworkApi : IArtworkApi
{
    private const string _api = nameof(IArtworkApi);
    private const long _limit = 25L * 1024 * 1024;

    private readonly FakeBackend _backend;
    private readonly Dictionary<string, string> _custom = [];
    private readonly Dictionary<string, (string? Wide, string? Square)> _finds = [];
    private readonly Dictionary<string, (string? Wide, string? Square)> _kept = [];
    private readonly Dictionary<string, int> _detected = [];

    internal FakeArtworkApi(FakeBackend backend)
    {
        _backend = backend;
    }

    public event EventHandler<ArtworkChange>? ArtworkChanged;

    private FakeState State => _backend.State;

    public Task<OperationResult<GameArtwork>> GetAsync(string gameId, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(GetAsync), [gameId], () => OperationResult.Success(Compose(State.Game(gameId))));

    public Task<OperationResult<GameArtwork>> SetCustomImageAsync(string gameId, string imagePath) =>
        _backend.Call(_api, nameof(SetCustomImageAsync), [gameId, imagePath], () =>
        {
            var game = State.Game(gameId);
            var file = new FileInfo(imagePath);
            if (!file.Exists)
            {
                throw new ArtworkNotAnImageException(imagePath);
            }

            if (file.Length > _limit)
            {
                throw new ArtworkTooLargeException(imagePath, 25);
            }

            if (ImageProbe.ReadFile(imagePath) is null)
            {
                throw new ArtworkNotAnImageException(imagePath);
            }

            _custom[gameId] = imagePath;
            RaiseChanged(gameId);
            return OperationResult.Success(Compose(game));
        });

    public Task<OperationResult<GameArtwork>> ClearCustomImageAsync(string gameId) =>
        _backend.Call(_api, nameof(ClearCustomImageAsync), [gameId], () =>
        {
            var game = State.Game(gameId);
            if (_custom.Remove(gameId))
            {
                RaiseChanged(gameId);
            }

            return OperationResult.Success(Compose(game));
        });

    public Task<OperationResult<GameArtwork>> LookAgainAsync(string gameId) =>
        _backend.Call(_api, nameof(LookAgainAsync), [gameId], () =>
        {
            var game = State.Game(gameId);
            if (_finds.TryGetValue(gameId, out var found) && _kept.GetValueOrDefault(gameId) != found)
            {
                _kept[gameId] = found;
                RaiseChanged(gameId);
            }

            return OperationResult.Success(Compose(game));
        });

    // What the next look again will find in the Steam client's cache or on its servers.
    public void ScriptSteamFinds(string gameId, string? wide, string? square) =>
        _backend.Script(() => _finds[gameId] = (wide, square));

    // An app id the fake's detection would find for the game when it has none stored.
    public void ScriptDetectedAppId(string gameId, int appId) =>
        _backend.Script(() => _detected[gameId] = appId);

    // Makes the game's Steam images appear as a lookup finishing in the background does: kept, and announced by the change event.
    public void ScriptSteamArrives(string gameId, string? wide, string? square) =>
        _backend.Script(() =>
        {
            _finds[gameId] = (wide, square);
            _kept[gameId] = (wide, square);
            RaiseChanged(gameId);
        });

    // Sets the user's own image without validating it, as a restored session would have it.
    public void ScriptCustom(string gameId, string path) =>
        _backend.Script(() =>
        {
            _custom[gameId] = path;
            RaiseChanged(gameId);
        });

    // Says the artwork changed without changing anything, as a lookup that wrote the same files would.
    public void ScriptChanged(string gameId) => _backend.Script(() => RaiseChanged(gameId));

    // Called under the backend's lock; the event fires once the outermost call releases it.
    private void RaiseChanged(string gameId) => _backend.Raise(() => ArtworkChanged?.Invoke(this, new ArtworkChange(gameId)));

    private GameArtwork Compose(Savepoint.Core.Catalog.GameDefinition game)
    {
        var detected = game.SteamAppId is null && _detected.TryGetValue(game.Id, out var found) ? found : (int?)null;
        var id = game.SteamAppId ?? detected;
        if (_custom.TryGetValue(game.Id, out var custom))
        {
            return new GameArtwork(game.Id, ArtworkSource.Custom, custom, custom, id, detected is not null);
        }

        if (_kept.TryGetValue(game.Id, out var steam) && (steam.Wide is not null || steam.Square is not null))
        {
            return new GameArtwork(game.Id, ArtworkSource.Steam, steam.Wide, steam.Square, id, detected is not null);
        }

        return new GameArtwork(game.Id, ArtworkSource.None, null, null, id, detected is not null);
    }
}
