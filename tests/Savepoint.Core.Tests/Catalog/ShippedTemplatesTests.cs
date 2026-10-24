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
