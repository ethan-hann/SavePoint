using System.Text;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Library;

namespace Savepoint.Core.Tests.Library;

// The records copy what GOG Galaxy's installer wrote on one PC (2026-10-24): Cyberpunk 2077 has one record for the game and two for its
// add-ons, all with the same folder, and the add-ons name the game they belong to and record no program.
public class GogInstalledGamesTests : IDisposable
{
    private readonly string _root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "savepoint-gog-" + Guid.NewGuid().ToString("N")));

    [Fact]
    public void An_installed_game_is_listed_with_its_folder_and_the_program_that_starts_it()
    {
        var folder = Folder("Deus Ex GOTY");
        Touch(folder, "system", "DeusEx.exe");

        var game = Assert.Single(GogInstalledGames.From([Deus(folder)]));

        Assert.Equal(LibrarySource.Gog, game.Source);
        Assert.Equal("1207658995", game.StoreId);
        Assert.Equal("Deus Ex GOTY", game.Name);
        Assert.Equal(folder, game.InstallPath);
        Assert.Equal("Deus Ex GOTY", game.FolderName);
        Assert.Equal(LaunchMethod.Exe, game.Launch.Method);
        Assert.Equal(Path.Combine("system", "DeusEx.exe"), game.Launch.ExePath);
        Assert.Null(game.SteamAppId);
    }

    [Fact]
    public void Add_ons_that_share_the_games_folder_are_not_listed_as_games()
    {
        var folder = Folder("Cyberpunk 2077");
        Touch(folder, "REDprelauncher.exe");
        var records = new[]
        {
            new GogInstallRecord("1256837418", "Cyberpunk 2077: Phantom Liberty", folder, DependsOn: "1423049311"),
            new GogInstallRecord("1423049311", "Cyberpunk 2077", folder, Path.Combine(folder, "REDprelauncher.exe"), "REDprelauncher.exe"),
            new GogInstallRecord("1597316373", "Cyberpunk 2077 REDmod", folder, DependsOn: "1423049311"),
        };

        var game = Assert.Single(GogInstalledGames.From(records));

        Assert.Equal("1423049311", game.StoreId);
        Assert.Equal("REDprelauncher.exe", game.Launch.ExePath);
    }

    [Fact]
    public void An_add_on_is_recognized_by_its_info_file_when_the_record_does_not_say()
    {
        var folder = Folder("Cyberpunk 2077");
        Touch(folder, "REDprelauncher.exe");
        File.WriteAllText(
            Path.Combine(folder, "goggame-1256837418.info"),
            "{\n    \"buildId\": \"58989493373906337\",\n    \"gameId\": \"1256837418\",\n    \"name\": \"Cyberpunk 2077: Phantom Liberty\",\n    \"playTasks\": [],\n    \"rootGameId\": \"1423049311\",\n    \"version\": 1\n}",
            new UTF8Encoding(false));
        var records = new[]
        {
            new GogInstallRecord("1256837418", "Cyberpunk 2077: Phantom Liberty", folder),
            new GogInstallRecord("1423049311", "Cyberpunk 2077", folder, Path.Combine(folder, "REDprelauncher.exe"), "REDprelauncher.exe"),
        };

        var games = GogInstalledGames.From(records);

        Assert.Equal(["1423049311"], games.Select(g => g.StoreId));
    }

    [Fact]
    public void A_game_whose_info_file_names_itself_as_the_root_is_a_game()
    {
        var folder = Folder("Cyberpunk 2077");
        Touch(folder, "REDprelauncher.exe");
        File.WriteAllText(Path.Combine(folder, "goggame-1423049311.info"), "{ \"gameId\": \"1423049311\", \"rootGameId\": \"1423049311\" }");

        var games = GogInstalledGames.From([new GogInstallRecord("1423049311", "Cyberpunk 2077", folder, Path.Combine(folder, "REDprelauncher.exe"), "REDprelauncher.exe")]);

        Assert.Equal(["1423049311"], games.Select(g => g.StoreId));
    }

    [Fact]
    public void A_record_whose_folder_is_gone_is_not_listed()
    {
        var gone = Path.Combine(_root, "Soldier of Fortune 2");

        var games = GogInstalledGames.From([new GogInstallRecord("1228964594", "Soldier of Fortune II: Double Helix", gone, Path.Combine(gone, "SoF2.exe"), "SoF2.exe")]);

        Assert.Empty(games);
    }

    [Fact]
    public void A_game_with_no_recorded_program_starts_through_galaxy()
    {
        var folder = Folder("Mystery");

        var game = Assert.Single(GogInstalledGames.From([new GogInstallRecord("42", "Mystery", folder)]));

        Assert.Equal(LaunchMethod.Uri, game.Launch.Method);
        Assert.Equal("goggalaxy://openGameView/42", game.Launch.Uri);
    }

    [Fact]
    public void A_program_that_is_not_in_the_folder_falls_back_to_the_file_name_when_it_is_there()
    {
        var folder = Folder("Soldier of Fortune 2");
        Touch(folder, "SoF2.exe");

        var game = Assert.Single(GogInstalledGames.From([new GogInstallRecord("1228964594", "Soldier of Fortune II", folder, Path.Combine(_root, "elsewhere", "SoF2.exe"), "SoF2.exe")]));

        Assert.Equal("SoF2.exe", game.Launch.ExePath);
    }

    [Fact]
    public void A_record_with_a_blank_id_or_name_or_folder_is_skipped()
    {
        var folder = Folder("Blank");

        var games = GogInstalledGames.From(
            [new GogInstallRecord("", "No Id", folder), new GogInstallRecord("7", " ", folder), new GogInstallRecord("8", "No Folder", " ")]);

        Assert.Empty(games);
    }

    [Fact]
    public void Games_come_back_sorted_by_name()
    {
        var zebra = Folder("Zebra");
        var alpha = Folder("Alpha");

        var games = GogInstalledGames.From([new GogInstallRecord("2", "Zebra Racing", zebra), new GogInstallRecord("1", "alpha Quest", alpha)]);

        Assert.Equal(["alpha Quest", "Zebra Racing"], games.Select(g => g.Name));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp folder left behind is harmless.
        }
    }

    private static GogInstallRecord Deus(string folder) =>
        new("1207658995", "Deus Ex GOTY", folder, Path.Combine(folder, "system", "DeusEx.exe"), "DeusEx.exe");

    private string Folder(string name)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Touch(string folder, params string[] parts)
    {
        var file = Path.Combine([folder, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "x");
    }
}
