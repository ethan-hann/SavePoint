using System.Security.Cryptography;
using Savepoint.Core.Api;
using Savepoint.Core.Artwork;
using Savepoint.Core.Catalog;
using Savepoint.Core.Tests.Backend;
using Savepoint.Core.Tests.Services;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Artwork;

// A Steam folder a test points the backend at: it holds what the test puts there, or nothing when the folder is null.
internal sealed class FixedSteamFolder(string? folder) : ISteamFolderLocator
{
    public string? Folder { get; set; } = folder;

    // How many lookups asked where Steam is, which is how a test sees that a lookup ran.
    public int Calls { get; private set; }

    public string? FindSteamFolder()
    {
        Calls++;
        return Folder;
    }
}

// A whole backend whose artwork side is faked: a Steam folder in a temp folder, a handler standing in for Steam's servers, and a clock
// the test can move. Games go straight into the catalog, which is all artwork needs and spares a git process per game.
internal sealed class ArtworkRig : IDisposable
{
    private readonly List<string> _changed = [];
    private readonly object _gate = new();

    public ArtworkRig(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respond = null, bool online = false, ManualTimeProvider? time = null, RecordingDiagnosticLog? log = null)
    {
        SteamRoot = Path.Combine(Path.GetTempPath(), "savepoint-steam-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(SteamRoot);
        Steam = new FixedSteamFolder(SteamRoot);
        Http = online ? new FakeHttpHandler(respond) : null;
        Time = time;
        Workspace = new BackendWorkspace(options => options with { SteamFolder = Steam, ArtworkHttp = Http, Time = time, Log = log ?? options.Log });
        Subscribe();
    }

    public BackendWorkspace Workspace { get; }

    public FixedSteamFolder Steam { get; }

    public string SteamRoot { get; }

    public FakeHttpHandler? Http { get; }

    public ManualTimeProvider? Time { get; }

    public IArtworkApi Artwork => Workspace.Backend.Artwork;

    public ArtworkApi Real => (ArtworkApi)Workspace.Backend.Artwork;

    public CancellationToken Token => Workspace.Token;

    // The ids of the games whose artwork the backend said had changed, in order.
    public IReadOnlyList<string> Changes
    {
        get
        {
            lock (_gate)
            {
                return [.. _changed];
            }
        }
    }

    // A second backend over the same folders, as after a restart: nothing the first one remembered in memory carries over.
    public void Restart()
    {
        Workspace.Restart();
        Subscribe();
    }

    public string ArtworkFolder(string gameId) => Path.Combine(Workspace.CatalogDirectory, "artwork", gameId);

    public string ArtworkRoot => Path.Combine(Workspace.CatalogDirectory, "artwork");

    // A game in the catalog, started from a link or a program, with whatever the test changes about it.
    public async Task<GameDefinition> AddGameAsync(string name = "Alpha", Func<GameDefinition, GameDefinition>? change = null)
    {
        var game = Workspace.NewGame(name, withSaves: false);
        game = change?.Invoke(game) ?? game;
        await Workspace.Backend.Services.Catalog.SaveGameAsync(game, Token);
        return game;
    }

    public async Task<GameDefinition> UpdateGameAsync(GameDefinition game)
    {
        await Workspace.Backend.Services.Catalog.SaveGameAsync(game, Token);
        return game;
    }

    // Puts Steam's own artwork for an app in the folder the fake Steam client uses.
    public void PutSteamCache(int appId, bool wide = true, bool square = true)
    {
        if (wide)
        {
            TestImages.Write(Path.Combine(SteamRoot, "appcache", "librarycache", appId.ToString()), "header.jpg", TestImages.Jpeg(460, 215));
        }

        if (square)
        {
            TestImages.Write(Path.Combine(SteamRoot, "appcache", "librarycache", appId.ToString()), "library_600x900.jpg", TestImages.Jpeg(600, 900));
        }
    }

    // A lookup holds its game's lookup gate from the moment the read that starts it returns until it ends, so taking the gate waits for it.
    public async Task WhenLookupDoneAsync(string gameId)
    {
        var gate = Real.LookupGateOf(gameId);
        await gate.WaitAsync(Token);
        gate.Release();
    }

    // Waits for the lookups of every game in the catalog.
    public async Task WhenIdleAsync()
    {
        foreach (var game in await Workspace.Backend.Services.Catalog.ListGamesAsync(Token))
        {
            await WhenLookupDoneAsync(game.Id);
        }
    }

    // Lets the lookup that Get starts finish, then reads again.
    public async Task<GameArtwork> LookedUpAsync(string gameId)
    {
        var first = await Artwork.GetAsync(gameId, Token);
        Assert.True(first.Ok, first.Error?.Detail);
        await WhenIdleAsync();
        var second = await Artwork.GetAsync(gameId, Token);
        Assert.True(second.Ok, second.Error?.Detail);
        await WhenIdleAsync();
        return second.Value;
    }

    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public void Dispose()
    {
        Workspace.Dispose();
        try
        {
            Directory.Delete(SteamRoot, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone.
        }
    }

    private void Subscribe() =>
        Artwork.ArtworkChanged += (_, change) =>
        {
            lock (_gate)
            {
                _changed.Add(change.GameId);
            }
        };
}
