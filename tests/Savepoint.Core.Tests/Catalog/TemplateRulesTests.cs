using Savepoint.Core.Catalog;
using static Savepoint.Core.Tests.Catalog.TemplateRules;

namespace Savepoint.Core.Tests.Catalog;

// The loops over every shipped template pass when nothing is wrong, so each rule is also tried on a template that breaks it.
public class TemplateRulesTests
{
    private static GameTemplate Game(GameDefinition definition, TemplateLibraryHints? hints = null) =>
        new(definition.Id, definition.Name, definition, hints);

    private static GameDefinition Definition(string id = "bad-game", string name = "Bad Game") => new(Id: id, Name: name);

    // What the rule says about the template, one line per problem, each starting with the template's id.
    private static string Found(GameTemplate template, Func<GameTemplate, IEnumerable<string>> rule) =>
        string.Join(Environment.NewLine, ProblemsIn([template], rule));

    private static Dictionary<string, ExternalLocation> Saves(string os, string path, ExternalRole role = ExternalRole.Saves) =>
        new() { [os] = new ExternalLocation(path, role) };

    [Fact]
    public void A_rule_names_the_template_and_the_problem()
    {
        var found = Found(Game(Definition(id: "Bad_Id")), IdProblems);

        Assert.Equal("Bad_Id: the id is not lowercase kebab-case", found);
    }

    [Fact]
    public void The_id_steam_and_store_rules_find_a_bad_value()
    {
        Assert.Contains("over 64", Found(Game(Definition(id: new string('a', 65))), IdProblems));
        Assert.Contains("steamAppId 0", Found(Game(Definition() with { SteamAppId = 0 }), SteamIdProblems));
        Assert.Contains("app id -5", Found(Game(Definition(), new TemplateLibraryHints(SteamAppIds: [-5])), SteamIdProblems));
        Assert.Contains("'12x'", Found(Game(Definition(), new TemplateLibraryHints(GogIds: ["12x"])), StoreNameProblems));
        Assert.Contains("'../up'", Found(Game(Definition(), new TemplateLibraryHints(SteamInstallDirs: ["../up"])), StoreNameProblems));
        Assert.Contains("'a/b'", Found(Game(Definition(), new TemplateLibraryHints(GogFolders: ["a/b"])), StoreNameProblems));
    }

    [Fact]
    public void The_saves_rules_find_a_folder_that_breaks_them()
    {
        var skyrim = GameTemplates.All.Single(template => template.Id == "skyrim-se");
        var withSaves = (string path) => Game(Definition() with { External = new ExternalLocation(path) });

        Assert.Contains("does not start with a path token", Found(withSaves(@"C:\Users\x\Saves"), template => ResolveProblems(SavesPaths(template), "external.path")));
        Assert.Contains("does not resolve", Found(withSaves(@"{noSuchToken}\Saves"), template => ResolveProblems(SavesPaths(template), "external.path")));
        Assert.Contains("climbs out", Found(withSaves(@"{documents}\..\Saves"), template => ResolveProblems(SavesPaths(template), "external.path")));
        Assert.Contains("role", Found(Game(Definition(), new TemplateLibraryHints(SavesByOs: Saves("linux", "{userHome}/g", (ExternalRole)99))), SavesRoleProblems));
        Assert.Contains("ends in a file name", Found(withSaves(@"{documents}\Game\slot1.sav"), template => FileNameProblems(SavesPaths(template), anyExtensionIsAFile: false)));
        Assert.Contains("wildcard", Found(withSaves(@"{documents}\Game\*"), template => FileNameProblems(SavesPaths(template), anyExtensionIsAFile: false)));
        Assert.Contains(skyrim.DisplayName, Found(withSaves($@"{{documents}}\{skyrim.DisplayName}\Saves"), template => OtherGameProblems(template, SavesPaths(template))));
        Assert.Contains("will not track", Found(withSaves("{appData}"), template => BroadFolderProblems(SavesPaths(template), "external.path")));
        Assert.Contains("'freebsd'", Found(Game(Definition(), new TemplateLibraryHints(SavesByOs: Saves("freebsd", "{userHome}/g"))), template => SystemNameProblems(template.Hints.SavesByOs.Keys)));
    }

    [Fact]
    public void The_mods_rules_find_a_folder_that_breaks_them()
    {
        var withMods = (string path) => Game(Definition() with { Mods = new ModsLocation(path) });

        Assert.Contains("does not resolve", Found(withMods(@"{noSuchToken}\Mods"), template => ResolveProblems(ModsPaths(template), "mods.path")));
        Assert.Contains("ends in a file name", Found(withMods(@"{appData}\Game\mods.zip"), template => FileNameProblems(ModsPaths(template), anyExtensionIsAFile: true)));
        Assert.Contains("will not track", Found(withMods("{appData}"), template => BroadFolderProblems(ModsPaths(template), "mods.path")));
    }

    [Fact]
    public void The_notes_link_and_validity_rules_find_a_template_that_breaks_them()
    {
        Assert.Contains("never shows", Found(Game(Definition() with { Notes = "Commit the branch" }), NoteProblems));
        Assert.Contains("not the link of Steam game 2", Found(Game(Definition() with { SteamAppId = 2, Launch = new LaunchConfig(LaunchMethod.Uri, Uri: "steam://rungameid/1") }), LinkProblems));
        Assert.Contains("install folder", Found(Game(Definition() with { Install = new InstallLocation(@"C:\Games") }), ValidityProblems));
        Assert.Contains("display name", Found(Game(Definition()) with { DisplayName = "Other" }, ValidityProblems));
    }

    [Fact]
    public void A_rule_finds_nothing_in_a_template_that_keeps_it()
    {
        var skyrim = GameTemplates.All.Single(template => template.Id == "skyrim-se");

        Assert.Empty(ProblemsIn([skyrim], template => ResolveProblems(SavesPaths(template), "external.path")));
        Assert.Empty(ProblemsIn([skyrim], ValidityProblems));
    }
}
