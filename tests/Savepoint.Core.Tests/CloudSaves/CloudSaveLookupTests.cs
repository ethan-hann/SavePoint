using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.CloudSaves;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.CloudSaves;

public class CloudSaveLookupTests
{
    private sealed class Scripted(CloudSaveState answer) : IStoreCloudReader
    {
        public List<string> Asked { get; } = [];

        public CloudSaveState Read(string storeGameId)
        {
            Asked.Add(storeGameId);
            return answer;
        }
    }

    private static GameDefinition Game(string install, int? steamAppId = null) => new()
    {
        Id = "g",
        Name = "G",
        Install = new InstallLocation(install),
        Launch = new LaunchConfig(LaunchMethod.Exe, "g.exe"),
        SteamAppId = steamAppId,
    };

    private static CloudSaveInfo Read(CloudSaveLookup lookup, GameDefinition game) =>
        lookup.Read(game, game.Install.Path, StringComparison.Ordinal);

    [Fact]
    public void A_stored_steam_app_id_is_asked_of_steam()
    {
        var steam = new Scripted(CloudSaveState.On);
        var galaxy = new Scripted(CloudSaveState.Off);

        var state = Read(new CloudSaveLookup(steam, galaxy), Game("/games/g", 730));

        Assert.Equal(new CloudSaveInfo(CloudSaveState.On, CloudStore.Steam), state);
        Assert.Equal(["730"], steam.Asked);
        Assert.Empty(galaxy.Asked);
    }

    [Fact]
    public void A_gog_product_in_the_install_folder_is_asked_of_galaxy()
    {
        using var install = new TempFolder();
        install.WriteText("goggame-1207658924.info", "{ \"gameId\": \"1207658924\", \"rootGameId\": \"1207658924\", \"playTasks\": [{}] }");
        var steam = new Scripted(CloudSaveState.On);
        var galaxy = new Scripted(CloudSaveState.Off);

        var state = Read(new CloudSaveLookup(steam, galaxy), Game(install.Path));

        Assert.Equal(new CloudSaveInfo(CloudSaveState.Off, CloudStore.Gog), state);
        Assert.Equal(["1207658924"], galaxy.Asked);
        Assert.Empty(steam.Asked);
    }

    [Fact]
    public void A_gog_game_without_a_galaxy_reader_is_unknown()
    {
        using var install = new TempFolder();
        install.WriteText("goggame-1207658924.info", "{ \"gameId\": \"1207658924\", \"rootGameId\": \"1207658924\", \"playTasks\": [{}] }");

        Assert.Equal(new CloudSaveInfo(CloudSaveState.Unknown, CloudStore.Gog), Read(new CloudSaveLookup(new Scripted(CloudSaveState.On), null), Game(install.Path)));
    }

    [Fact]
    public void A_steam_game_found_in_a_library_is_asked_of_steam()
    {
        using var library = new TempFolder();
        library.WriteText("steamapps/appmanifest_220.acf", "\"AppState\"\n{\n\t\"appid\"\t\t\"220\"\n\t\"installdir\"\t\t\"Half-Life 2\"\n}\n");
        var install = Directory.CreateDirectory(library.Combine("steamapps", "common", "Half-Life 2")).FullName;
        var steam = new Scripted(CloudSaveState.On);

        var state = Read(new CloudSaveLookup(steam, null), Game(install));

        Assert.Equal(new CloudSaveInfo(CloudSaveState.On, CloudStore.Steam), state);
        Assert.Equal(["220"], steam.Asked);
    }

    [Fact]
    public void A_game_from_no_known_store_is_unknown()
    {
        using var install = new TempFolder();
        var steam = new Scripted(CloudSaveState.On);

        Assert.Equal(new CloudSaveInfo(CloudSaveState.Unknown, CloudStore.None), Read(new CloudSaveLookup(steam, new Scripted(CloudSaveState.On)), Game(install.Path)));
        Assert.Empty(steam.Asked);
    }

    [Fact]
    public void A_stored_steam_id_wins_over_a_gog_product()
    {
        using var install = new TempFolder();
        install.WriteText("goggame-1207658924.info", "{ \"gameId\": \"1207658924\", \"rootGameId\": \"1207658924\", \"playTasks\": [{}] }");
        var steam = new Scripted(CloudSaveState.Off);

        Assert.Equal(new CloudSaveInfo(CloudSaveState.Off, CloudStore.Steam), Read(new CloudSaveLookup(steam, new Scripted(CloudSaveState.On)), Game(install.Path, 730)));
    }
}
