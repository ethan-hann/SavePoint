using System.Text.Json;
using System.Text.RegularExpressions;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;
using Savepoint.Core.Tests.Platform;

namespace Savepoint.Core.Tests.Catalog;

// Checks every template that ships, so a new file with a wrong token, id, or note fails here and not in a player's hands.
public partial class ShippedTemplatesTests
{
    private const string _resourcePrefix = "Savepoint.Core.Catalog.Templates.";

    public static TheoryData<string> TemplateIds => new(GameTemplates.All.Select(t => t.Id));

    private static GameTemplate Template(string id) => GameTemplates.All.Single(t => t.Id == id);

    private static IPlatform PlatformFor(string os) => os switch
    {
        "windows" => new WindowsPlatform(FakeEnvironment.Windows().Build()),
        "linux" => new LinuxPlatform(FakeEnvironment.Linux().Build()),
        "macos" => new MacOSPlatform(FakeEnvironment.MacOS().Build()),
        _ => throw new ArgumentOutOfRangeException(nameof(os), os, "Not one of the three systems."),
    };

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex KebabCase();

    [GeneratedRegex(@"\b(commits?|branch(es)?|checkout|repos?|repositor(y|ies))\b|\bHEAD\b|—", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BannedInNotes();

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

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Id_is_lowercase_kebab_case_and_names_the_file(string id)
    {
        Assert.Matches(KebabCase(), id);
        Assert.True(id.Length <= 64);
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Steam_ids_are_positive(string id)
    {
        var template = Template(id);

        Assert.True(template.Definition.SteamAppId is null or > 0);
        Assert.All(template.Hints.SteamAppIds, appId => Assert.True(appId > 0));
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Store_ids_and_folder_names_are_single_safe_names(string id)
    {
        var hints = Template(id).Hints;

        Assert.All(hints.GogIds, gog => Assert.Matches(@"^[0-9]+\z", gog));
        Assert.All(
            hints.SteamInstallDirs.Concat(hints.GogFolders),
            folder =>
            {
                Assert.False(string.IsNullOrWhiteSpace(folder));
                Assert.DoesNotContain('/', folder);
                Assert.DoesNotContain('\\', folder);
                Assert.DoesNotContain("..", folder);
                Assert.Equal(folder.Trim(), folder);
            });
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Saves_folders_resolve_on_the_system_they_are_written_for(string id)
    {
        var template = Template(id);

        if (template.Definition.External is { } external)
        {
            AssertResolves("windows", external);
        }

        foreach (var (os, location) in template.Hints.SavesByOs)
        {
            AssertResolves(os, location);
        }
    }

    private static void AssertResolves(string os, ExternalLocation location)
    {
        Assert.Equal(ExternalRole.Saves, location.Role);
        Assert.Matches(@"^\{[A-Za-z]+\}", location.Path);
        Assert.DoesNotContain("..", location.Path);
        var resolver = new PathTokenResolver(PlatformFor(os));
        var resolved = Record.Exception(() => resolver.Resolve(location.Path, "external.path"));
        var issues = (resolved as ValidationException)?.Args["issues"] as IReadOnlyList<ValidationIssue>;

        Assert.True(resolved is null, $"'{location.Path}' does not resolve on {os}: {issues?.FirstOrDefault()?.MessageId ?? resolved?.Message}");
    }

    // Every saves folder a template names, with the system it is for.
    private static IEnumerable<(string Os, string Path)> SavesPaths(GameTemplate template)
    {
        if (template.Definition.External is { } external)
        {
            yield return ("windows", external.Path);
        }

        foreach (var (os, location) in template.Hints.SavesByOs)
        {
            yield return (os, location.Path);
        }
    }

    private static string[] Segments(string path) => path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    // Letters and digits only, so "Crysis2Remastered" and "Crysis 2 Remastered" compare equal.
    private static string Squash(string text) => NotAlphanumeric().Replace(Years().Replace(text, string.Empty), string.Empty).ToLowerInvariant();

    [GeneratedRegex(@"[^A-Za-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NotAlphanumeric();

    [GeneratedRegex(@"\(\d{4}\)", RegexOptions.CultureInvariant)]
    private static partial Regex Years();

    private static readonly HashSet<string> _fileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "sav", "save", "dat", "bin", "json", "xml", "ini", "cfg", "conf", "lua", "db", "sl2", "txt", "log", "bak", "data", "fsav",
        "b", "yml", "yaml", "plist", "sdt", "ess", "es3", "sqlite", "png", "jpg",
    };

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void A_saves_folder_does_not_end_in_a_file_name_or_a_wildcard(string id)
    {
        foreach (var (os, path) in SavesPaths(Template(id)))
        {
            var last = Segments(path)[^1];
            var extension = Path.GetExtension(last).TrimStart('.');

            Assert.DoesNotContain('*', path);
            Assert.DoesNotContain('?', path);
            Assert.False(extension.Length > 0 && _fileExtensions.Contains(extension), $"'{path}' ({os}) ends in a file name, not a folder.");
        }
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void No_saves_folder_is_another_games_folder(string id)
    {
        var template = Template(id);
        var own = Squash(template.DisplayName);
        var others = GameTemplates.All.Where(other => other.Id != id).Select(other => (other.DisplayName, Squashed: Squash(other.DisplayName)))
            .Where(other => other.Squashed.Length > 0 && !own.Contains(other.Squashed, StringComparison.Ordinal))
            .ToList();

        foreach (var (os, path) in SavesPaths(template))
        {
            var named = Segments(path).Select(Squash).ToHashSet();
            var clash = others.FirstOrDefault(other => named.Contains(other.Squashed));

            Assert.True(clash.DisplayName is null, $"'{path}' ({os}) is a folder of '{clash.DisplayName}', not of '{template.DisplayName}'.");
        }
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Linux_and_macos_drafts_use_only_their_own_entry(string id)
    {
        var template = Template(id);

        foreach (var (os, key) in new[] { (OsKind.Linux, "linux"), (OsKind.MacOS, "macos") })
        {
            var expected = template.Hints.SavesByOs.TryGetValue(key, out var entry) ? entry : null;

            Assert.Equal(expected, template.ToDraft(os).External);
        }
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void A_saves_folder_is_not_one_of_the_platforms_broad_folders(string id)
    {
        foreach (var (os, path) in SavesPaths(Template(id)))
        {
            AssertNotBroad(os, path);
        }
    }

    private static void AssertNotBroad(string os, string path)
    {
        var platform = PlatformFor(os);
        var resolved = new PathTokenResolver(platform).Resolve(path, "external.path");
        var comparison = platform.PathComparison;
        var separators = new[] { '/', '\\' };
        foreach (var entry in platform.Denylist([]).Entries)
        {
            var blocked = entry.Path.TrimEnd(separators);
            var here = resolved.TrimEnd(separators);
            var inside = entry.Tier == DenylistTier.Subtree
                && here.StartsWith(blocked + PlatformSeparator(os), comparison)
                && !(entry.AllowedSubRoots ?? []).Any(root => here.StartsWith(root.TrimEnd(separators), comparison));
            var equal = !entry.ChildrenOnly && string.Equals(here, blocked, comparison);

            Assert.False(equal || inside, $"'{path}' ({os}) is {resolved}, which SavePoint will not track: {entry.Reason}");
        }
    }

    private static char PlatformSeparator(string os) => os == "windows" ? '\\' : '/';

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Per_system_saves_use_only_the_three_system_names(string id)
    {
        Assert.All(Template(id).Hints.SavesByOs.Keys, key => Assert.Contains(key, new[] { "windows", "linux", "macos" }));
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Notes_use_plain_words(string id)
    {
        var notes = Template(id).Definition.Notes;

        Assert.True(notes is null || !BannedInNotes().IsMatch(notes), $"Notes for '{id}' use a word the app never shows: {notes}");
    }

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

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void A_template_that_starts_through_a_link_names_a_steam_game(string id)
    {
        var definition = Template(id).Definition;

        if (definition.Launch.Method == LaunchMethod.Uri)
        {
            Assert.Equal($"steam://rungameid/{definition.SteamAppId}", definition.Launch.Uri);
        }
    }
}
