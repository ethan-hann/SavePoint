using System.Text.Json;
using Savepoint.Core.Catalog;
using static Savepoint.Core.Tests.Catalog.TemplateRules;

namespace Savepoint.Core.Tests.Catalog;

// Checks every template that ships, so a new file with a wrong token, id, or note fails here and not in a player's hands. Each rule that
// covers every template is one test over all of them, and its failure lists every template that breaks it and why.
public class ShippedTemplatesTests
{
    private const string _resourcePrefix = "Savepoint.Core.Catalog.Templates.";

    private static GameTemplate Template(string id) => GameTemplates.All.Single(t => t.Id == id);

    [Fact]
    public void Every_embedded_template_file_is_loaded()
    {
        var embedded = typeof(GameTemplates).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(_resourcePrefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            .Select(name => name[_resourcePrefix.Length..^".json".Length])
            .Order(StringComparer.Ordinal);

        Assert.Equal(embedded, GameTemplates.All.Select(t => t.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void No_template_file_repeats_a_key()
    {
        // The loader keeps the last of two equal keys without a word, so a copy-and-paste slip would change a template silently.
        var assembly = typeof(GameTemplates).Assembly;
        var options = new JsonDocumentOptions { AllowDuplicateProperties = false };
        var problems = new List<string>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(_resourcePrefix, StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            try
            {
                using var document = JsonDocument.Parse(reader.ReadToEnd(), options);
            }
            catch (JsonException exception)
            {
                problems.Add($"{resource[_resourcePrefix.Length..]}: {exception.Message}");
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void Games_checked_against_their_wiki_page_keep_their_corrected_facts()
    {
        Assert.Equal(@"{documents}\Saved Games\Hades", Template("hades").Definition.External!.Path);
        Assert.Equal(1659040, Template("hitman-world-of-assassination").Definition.SteamAppId);
        Assert.Empty(Template("hitman-world-of-assassination").Hints.GogIds);
        Assert.Equal("steam://rungameid/1659040", Template("hitman-world-of-assassination").Definition.Launch.Uri);
        Assert.Equal(1091500, Template("cyberpunk-2077").Definition.SteamAppId);
        Assert.Equal(@"{localLow}\Ludeon Studios\RimWorld by Ludeon Studios\Saves", Template("rimworld").Definition.External!.Path);
        Assert.Empty(Template("rise-of-the-tomb-raider").Hints.SavesByOs);
        Assert.DoesNotContain("macos", Template("batman-arkham-asylum-goty-edition").Hints.SavesByOs.Keys);
        Assert.Equal("Dead Space (2008)", Template("dead-space").DisplayName);
        Assert.Equal("Lords of the Fallen (2014)", Template("lords-of-the-fallen").DisplayName);
    }

    [Fact]
    public void A_steam_row_in_the_userdata_folder_leaves_the_saves_folder_blank()
    {
        // The wiki gives these games a Steam-only location the tokens cannot name, and no Windows folder was confirmed on disk.
        foreach (var id in new[] { "valheim", "terraria", "cuphead", "transport-fever-2", "grim-dawn", "frostpunk" })
        {
            Assert.Null(Template(id).Definition.External);
            Assert.Contains("Steam's userdata folder", Template(id).Definition.Notes);
        }
    }

    [Fact]
    public void Template_ids_are_unique_and_names_are_unique()
    {
        var ids = GameTemplates.All.Select(t => t.Id).ToList();
        var names = GameTemplates.All.Select(t => t.DisplayName.ToLowerInvariant()).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void No_store_id_belongs_to_two_templates()
    {
        var steam = GameTemplates.All.SelectMany(t => t.Hints.SteamAppIds.Concat(t.Definition.SteamAppId is { } id ? [id] : [])).ToList();
        var gog = GameTemplates.All.SelectMany(t => t.Hints.GogIds).ToList();

        Assert.Equal(steam.Count, steam.Distinct().Count());
        Assert.Equal(gog.Count, gog.Distinct().Count());
    }

    [Fact]
    public void Every_id_is_lowercase_kebab_case_and_at_most_64_characters() => AssertShippedTemplatesKeep(IdProblems);

    [Fact]
    public void Every_steam_id_is_positive() => AssertShippedTemplatesKeep(SteamIdProblems);

    [Fact]
    public void Store_ids_and_folder_names_are_single_safe_names() => AssertShippedTemplatesKeep(StoreNameProblems);

    [Fact]
    public void Enough_templates_name_a_saves_folder_for_the_saves_rules_to_mean_something()
    {
        // The saves rules below loop over these paths, so an enumeration that came back empty would let them all pass.
        Assert.True(GameTemplates.All.SelectMany(SavesPaths).Count() >= 100);
    }

    [Fact]
    public void Saves_folders_resolve_on_the_system_they_are_written_for() =>
        AssertShippedTemplatesKeep(template => SavesRoleProblems(template).Concat(ResolveProblems(SavesPaths(template), "external.path")));

    [Fact]
    public void A_saves_folder_does_not_end_in_a_file_name_or_a_wildcard() =>
        AssertShippedTemplatesKeep(template => FileNameProblems(SavesPaths(template), anyExtensionIsAFile: false));

    [Fact]
    public void No_saves_folder_is_another_games_folder() =>
        AssertShippedTemplatesKeep(template => OtherGameProblems(template, SavesPaths(template)));

    [Fact]
    public void A_saves_folder_is_not_one_of_the_platforms_broad_folders() =>
        AssertShippedTemplatesKeep(template => BroadFolderProblems(SavesPaths(template), "external.path"));

    [Fact]
    public void Per_system_saves_use_only_the_three_system_names() =>
        AssertShippedTemplatesKeep(template => SystemNameProblems(template.Hints.SavesByOs.Keys));

    [Fact]
    public void Notes_use_plain_words() => AssertShippedTemplatesKeep(NoteProblems);

    // Whether a game's cloud saves are on is the editor's call, made per game; a note cannot know it.
    [Fact]
    public void Notes_say_nothing_about_cloud_saves()
    {
        var offenders = GameTemplates.All
            .Where(template => template.Definition.Notes is { } notes && notes.Contains("cloud", StringComparison.OrdinalIgnoreCase))
            .Select(template => template.Id)
            .ToList();

        Assert.True(offenders.Count == 0, $"{offenders.Count} notes mention cloud saves, such as: {string.Join(", ", offenders.Take(5))}");
    }

    [Fact]
    public void A_template_that_starts_through_a_link_names_a_steam_game() => AssertShippedTemplatesKeep(LinkProblems);
}
