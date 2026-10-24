using System.Text.Json;
using Savepoint.Core.Catalog;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Catalog;

public class TemplateLibraryHintsTests
{
    private const string _withHints = """
        {
          "schemaVersion": 1,
          "id": "alpha",
          "name": "Alpha Quest",
          "steamAppId": 4242,
          "external": { "path": "{documents}\\Alpha\\Saves", "role": "saves" },
          "launch": { "method": "exe", "exePath": "alpha.exe", "args": [] },
          "libraryHints": {
            "steamAppIds": [4243, 4244],
            "steamInstallDirs": ["Alpha Quest", "AlphaQuestGOTY"],
            "gogIds": ["1000001"],
            "gogFolders": ["Alpha Quest GOG"],
            "savesByOs": {
              "linux": { "path": "{userHome}/.local/share/Alpha/saves", "role": "saves" },
              "macos": { "path": "{userHome}/Library/Application Support/Alpha/saves", "role": "saves" }
            }
          }
        }
        """;

    private const string _withoutHints = """
        {
          "schemaVersion": 1,
          "id": "beta",
          "name": "Beta Quest",
          "external": { "path": "{documents}\\Beta", "role": "saves" },
          "launch": { "method": "exe", "exePath": "beta.exe", "args": [] }
        }
        """;

    [Fact]
    public void A_template_without_a_hints_block_has_empty_hints()
    {
        var template = GameTemplates.Read(_withoutHints);

        Assert.Empty(template.Hints.SteamAppIds);
        Assert.Empty(template.Hints.SteamInstallDirs);
        Assert.Empty(template.Hints.GogIds);
        Assert.Empty(template.Hints.GogFolders);
        Assert.Empty(template.Hints.SavesByOs);
    }

    [Fact]
    public void The_hints_block_is_read_next_to_the_definition()
    {
        var template = GameTemplates.Read(_withHints);

        Assert.Equal([4243, 4244], template.Hints.SteamAppIds);
        Assert.Equal(["Alpha Quest", "AlphaQuestGOTY"], template.Hints.SteamInstallDirs);
        Assert.Equal(["1000001"], template.Hints.GogIds);
        Assert.Equal(["Alpha Quest GOG"], template.Hints.GogFolders);
        Assert.Equal(4242, template.Definition.SteamAppId);
    }

    [Fact]
    public void The_hints_never_become_part_of_the_stored_definition()
    {
        var template = GameTemplates.Read(_withHints);

        var stored = JsonSerializer.Serialize(template.Definition, CatalogJsonContext.Default.GameDefinition);

        Assert.DoesNotContain("libraryHints", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("gogIds", stored, StringComparison.Ordinal);
    }

    [Fact]
    public void A_draft_uses_the_saves_folder_written_for_its_operating_system()
    {
        var template = GameTemplates.Read(_withHints);

        Assert.Equal("{userHome}/.local/share/Alpha/saves", template.ToDraft(OsKind.Linux).External!.Path);
        Assert.Equal("{userHome}/Library/Application Support/Alpha/saves", template.ToDraft(OsKind.MacOS).External!.Path);
    }

    [Fact]
    public void A_draft_falls_back_to_the_main_saves_folder_when_its_operating_system_has_none()
    {
        var template = GameTemplates.Read(_withHints);

        Assert.Equal(@"{documents}\Alpha\Saves", template.ToDraft(OsKind.Windows).External!.Path);
    }

    [Fact]
    public void A_draft_keeps_the_old_shape_when_there_are_no_per_system_folders()
    {
        var template = GameTemplates.Read(_withoutHints);

        var draft = template.ToDraft(OsKind.Windows);

        Assert.Equal(template.Definition with { Id = draft.Id }, draft);
        Assert.Equal(template.Definition with { Id = draft.Id, External = null }, template.ToDraft(OsKind.Linux) with { Id = draft.Id });
    }

    [Fact]
    public void Hint_lists_are_read_only_so_a_caller_cannot_change_the_shared_template()
    {
        var hints = GameTemplates.Read(_withHints).Hints;

        Assert.True(((ICollection<string>)hints.SteamInstallDirs).IsReadOnly);
        Assert.True(((ICollection<string>)hints.GogIds).IsReadOnly);
        Assert.True(((ICollection<int>)hints.SteamAppIds).IsReadOnly);
    }

    [Fact]
    public void The_skyrim_template_names_its_steam_folder_and_app_id()
    {
        var skyrim = GameTemplates.All.Single(t => t.Id == "skyrim-se");

        Assert.Equal(489830, skyrim.Definition.SteamAppId);
        Assert.Contains("Skyrim Special Edition", skyrim.Hints.SteamInstallDirs);
    }
}
