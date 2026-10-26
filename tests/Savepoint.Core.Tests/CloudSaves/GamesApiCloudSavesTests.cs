using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Tests.Artwork;
using Savepoint.Core.Tests.Backend;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.CloudSaves;

// The whole path from a catalog game to the answer, over folders a test made: no real Steam or Galaxy folder is read.
public class GamesApiCloudSavesTests : IDisposable
{
    private readonly TempFolder _steam = new();
    private readonly TempFolder _programData = new();
    private readonly TempFolder _localAppData = new();

    public void Dispose()
    {
        _steam.Dispose();
        _programData.Dispose();
        _localAppData.Dispose();
    }

    private BackendWorkspace Workspace() => new(options => options with
    {
        SteamFolder = new FixedSteamFolder(_steam.Path),
        GalaxyProgramData = _programData.Path,
        GalaxyLocalAppData = _localAppData.Path,
    });

    private static async Task<GameDefinition> AddAsync(BackendWorkspace workspace, Func<GameDefinition, GameDefinition> change)
    {
        var game = change(workspace.NewGame("Alpha", withSaves: false));
        await workspace.Backend.Services.Catalog.SaveGameAsync(game, workspace.Token);
        return game;
    }

    [Fact]
    public async Task A_steam_game_reports_its_cloud_switch()
    {
        _steam.WriteText("config/loginusers.vdf", "\"users\"\n{\n\t\"76561198000000001\"\n\t{\n\t\t\"MostRecent\"\t\t\"1\"\n\t}\n}\n");
        _steam.WriteText(
            "userdata/39734273/7/remote/sharedconfig.vdf",
            "\"UserRoamingConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n\t\t\t\t\"apps\"\n\t\t\t\t{\n\t\t\t\t\t\"730\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"cloudenabled\"\t\t\"1\"\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n");
        using var workspace = Workspace();
        var game = await AddAsync(workspace, g => g with { SteamAppId = 730 });

        var state = await workspace.Games.GetCloudSavesAsync(game.Id, workspace.Token);

        Assert.Equal(CloudSaveState.On, state.Value);
    }

    [Fact]
    public async Task A_gog_game_follows_the_galaxy_global_switch_on_windows_and_is_unknown_elsewhere()
    {
        _localAppData.WriteText("GOG.com/Galaxy/Configuration/config.json", "{ \"featureCloudSaves\": false }");
        using var workspace = Workspace();
        var game = await AddAsync(workspace, g =>
        {
            File.WriteAllText(Path.Combine(g.Install.Path, "goggame-1207658924.info"), "{ \"gameId\": \"1207658924\", \"rootGameId\": \"1207658924\", \"playTasks\": [{}] }");
            return g;
        });

        var state = await workspace.Games.GetCloudSavesAsync(game.Id, workspace.Token);

        Assert.Equal(OperatingSystem.IsWindows() ? CloudSaveState.Off : CloudSaveState.Unknown, state.Value);
    }

    [Fact]
    public async Task A_game_the_catalog_does_not_hold_fails_as_not_found()
    {
        using var workspace = Workspace();

        var state = await workspace.Games.GetCloudSavesAsync("missing", workspace.Token);

        Assert.False(state.Ok);
        Assert.Equal("error.game.notFound", state.Error.MessageId);
    }
}
