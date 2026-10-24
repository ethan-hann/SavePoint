using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Library;

namespace Savepoint.Core.Tests.Library;

public class TemplateMatcherTests
{
    private static GameTemplate Template(string id, int? steamAppId = null, TemplateLibraryHints? hints = null) =>
        new(id, id, new GameDefinition(Id: id, Name: id, SteamAppId: steamAppId), hints);

    private static InstalledGame Steam(int appId, string folder) =>
        new(LibrarySource.Steam, appId.ToString(), "Name", "/x/" + folder, folder, new LaunchConfig(), appId);

    private static InstalledGame Gog(string id, string folder) =>
        new(LibrarySource.Gog, id, "Name", "/x/" + folder, folder, new LaunchConfig(), null);

    [Fact]
    public void A_steam_game_matches_the_template_with_its_app_id()
    {
        var templates = new[] { Template("other", 1), Template("skyrim", 489830) };

        Assert.Equal("skyrim", TemplateMatcher.Find(templates, Steam(489830, "Whatever"))?.Id);
    }

    [Fact]
    public void A_steam_game_matches_an_extra_app_id_in_the_hints()
    {
        var templates = new[] { Template("skyrim", 489830, new TemplateLibraryHints(SteamAppIds: [1946180])) };

        Assert.Equal("skyrim", TemplateMatcher.Find(templates, Steam(1946180, "Whatever"))?.Id);
    }

    [Fact]
    public void A_steam_game_matches_by_install_folder_name_ignoring_case()
    {
        var templates = new[] { Template("skyrim", 489830, new TemplateLibraryHints(SteamInstallDirs: ["Skyrim Special Edition"])) };

        Assert.Equal("skyrim", TemplateMatcher.Find(templates, Steam(777, "skyrim special edition"))?.Id);
    }

    [Fact]
    public void The_app_id_wins_over_a_folder_name_even_when_the_folder_template_comes_first()
    {
        var templates = new[]
        {
            Template("by-folder", 1, new TemplateLibraryHints(SteamInstallDirs: ["Shared Folder"])),
            Template("by-id", 2),
        };

        Assert.Equal("by-id", TemplateMatcher.Find(templates, Steam(2, "Shared Folder"))?.Id);
    }

    [Fact]
    public void A_gog_game_matches_by_product_id_then_by_folder_name()
    {
        var templates = new[]
        {
            Template("by-folder", hints: new TemplateLibraryHints(GogFolders: ["Deus Ex GOTY"])),
            Template("by-id", hints: new TemplateLibraryHints(GogIds: ["1207658995"])),
        };

        Assert.Equal("by-id", TemplateMatcher.Find(templates, Gog("1207658995", "Deus Ex GOTY"))?.Id);
        Assert.Equal("by-folder", TemplateMatcher.Find(templates, Gog("5", "deus ex goty"))?.Id);
    }

    [Fact]
    public void A_game_nothing_describes_has_no_template()
    {
        var templates = new[] { Template("skyrim", 489830, new TemplateLibraryHints(SteamInstallDirs: ["Skyrim Special Edition"], GogIds: ["9"])) };

        Assert.Null(TemplateMatcher.Find(templates, Steam(1, "Other")));
        Assert.Null(TemplateMatcher.Find(templates, Gog("1", "Other")));
    }

    [Fact]
    public void Steam_hints_do_not_match_a_gog_game_and_gog_hints_do_not_match_a_steam_game()
    {
        var templates = new[] { Template("t", 489830, new TemplateLibraryHints(SteamInstallDirs: ["Shared"], GogFolders: ["Other Folder"])) };

        Assert.Null(TemplateMatcher.Find(templates, Gog("489830", "Shared")));
        Assert.Null(TemplateMatcher.Find(templates, Steam(5, "Other Folder")));
    }
}
