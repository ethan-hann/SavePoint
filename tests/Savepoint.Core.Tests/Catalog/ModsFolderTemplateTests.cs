using Savepoint.Core.Catalog;
using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using static Savepoint.Core.Tests.Catalog.TemplateRules;

namespace Savepoint.Core.Tests.Catalog;

// A template can name the mods folder a game keeps outside its install, the same way it names the saves folder.
public class ModsFolderTemplateTests
{
    private const string _withMods = """
        {
          "schemaVersion": 1,
          "id": "alpha",
          "name": "Alpha",
          "libraryHints": {
            "modsByOs": {
              "Linux": { "path": "{userHome}/.local/share/Alpha/Mods" },
              "macos": { "path": "{userHome}/Library/Application Support/Alpha/Mods" }
            }
          },
          "mods": { "path": "{appData}\\Alpha\\Mods" }
        }
        """;

    private static GameTemplate Template(string id) => GameTemplates.All.Single(template => template.Id == id);

    [Fact]
    public void A_draft_has_the_mods_folder_of_the_template_on_windows()
    {
        var draft = GameTemplates.Read(_withMods).ToDraft(OsKind.Windows);

        Assert.Equal(@"{appData}\Alpha\Mods", draft.Mods!.Path);
    }

    [Fact]
    public void A_draft_uses_the_mods_folder_written_for_its_operating_system()
    {
        var template = GameTemplates.Read(_withMods);

        Assert.Equal("{userHome}/.local/share/Alpha/Mods", template.ToDraft(OsKind.Linux).Mods!.Path);
        Assert.Equal("{userHome}/Library/Application Support/Alpha/Mods", template.ToDraft(OsKind.MacOS).Mods!.Path);
    }

    [Fact]
    public void A_draft_leaves_the_mods_folder_blank_on_a_system_the_template_names_none_for()
    {
        var template = GameTemplates.Read(_withMods) with { Hints = new TemplateLibraryHints() };

        Assert.Null(template.ToDraft(OsKind.Linux).Mods);
        Assert.Null(template.ToDraft(OsKind.MacOS).Mods);
        Assert.NotNull(template.ToDraft(OsKind.Windows).Mods);
    }

    [Fact]
    public void The_per_system_mods_folders_are_read_by_lowercase_system_name()
    {
        var hints = GameTemplates.Read(_withMods).Hints;

        Assert.Equal(["linux", "macos"], hints.ModsByOs.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_template_without_a_mods_folder_gives_a_draft_without_one_on_every_system()
    {
        var template = GameTemplates.All.First(candidate => candidate.Definition.Mods is null && candidate.Hints.ModsByOs.Count == 0);

        Assert.All(new[] { OsKind.Windows, OsKind.Linux, OsKind.MacOS }, os => Assert.Null(template.ToDraft(os).Mods));
    }

    [Fact]
    public void Seven_days_to_die_carries_its_mods_folder_on_every_system()
    {
        var template = Template("7-days-to-die");

        Assert.Equal(@"{appData}\7DaysToDie\Mods", template.ToDraft(OsKind.Windows).Mods!.Path);
        Assert.Equal("{userHome}/.local/share/7DaysToDie/Mods", template.ToDraft(OsKind.Linux).Mods!.Path);
        Assert.Equal("{userHome}/Library/Application Support/7DaysToDie/Mods", template.ToDraft(OsKind.MacOS).Mods!.Path);
        Assert.Equal(["Mods"], template.Definition.ModPathHints);
    }

    [Fact]
    public void Seven_days_to_die_mods_folder_on_macos_is_the_mods_folder_next_to_the_saves_in_the_games_application_support_folder()
    {
        var draft = Template("7-days-to-die").ToDraft(OsKind.MacOS);
        var tokens = new PathTokenResolver(PlatformFor("macos"));

        var mods = tokens.Resolve(draft.Mods!.Path, "mods.path");
        var saves = tokens.Resolve(draft.External!.Path, "external.path");

        // The game's own data folder on a Mac is ~/Library/Application Support/7DaysToDie, where the saves sit too; no Mac has checked this here.
        Assert.Equal("/Users/test/Library/Application Support/7DaysToDie/Mods", mods);
        Assert.Equal("/Users/test/Library/Application Support/7DaysToDie", Path.GetDirectoryName(mods)?.Replace('\\', '/'));
        Assert.Equal(Path.GetDirectoryName(saves)?.Replace('\\', '/'), Path.GetDirectoryName(mods)?.Replace('\\', '/'));
    }

    [Fact]
    public void The_mods_folder_of_a_draft_is_never_inside_or_around_its_saves_folder()
    {
        var template = Template("7-days-to-die");

        foreach (var os in new[] { OsKind.Windows, OsKind.Linux, OsKind.MacOS })
        {
            var draft = template.ToDraft(os);

            Assert.Empty(CatalogValidator.Validate(draft with { Install = new InstallLocation("{userHome}/Games/7dtd") }));
        }
    }

    // Only one template names a mods folder today, so the rules below see its three paths. The shipped-data count in ShippedTemplatesTests
    // fails when any of the three systems has none, so these loops cannot pass over nothing.
    [Fact]
    public void Mods_folders_resolve_on_the_system_they_are_written_for() =>
        AssertShippedTemplatesKeep(template => ResolveProblems(ModsPaths(template), "mods.path"));

    [Fact]
    public void A_mods_folder_does_not_end_in_a_file_name_or_a_wildcard() =>
        AssertShippedTemplatesKeep(template => FileNameProblems(ModsPaths(template), anyExtensionIsAFile: true));

    [Fact]
    public void No_mods_folder_is_another_games_folder() =>
        AssertShippedTemplatesKeep(template => OtherGameProblems(template, ModsPaths(template)));

    [Fact]
    public void A_mods_folder_is_not_one_of_the_platforms_broad_folders() =>
        AssertShippedTemplatesKeep(template => BroadFolderProblems(ModsPaths(template), "mods.path"));

    [Fact]
    public void Per_system_mods_use_only_the_three_system_names() =>
        AssertShippedTemplatesKeep(template => SystemNameProblems(template.Hints.ModsByOs.Keys));
}
