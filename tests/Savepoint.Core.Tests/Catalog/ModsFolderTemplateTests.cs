using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;
using Savepoint.Core.Tests.Platform;

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

    public static TheoryData<string> TemplateIds => new(GameTemplates.All.Select(template => template.Id));

    private static GameTemplate Template(string id) => GameTemplates.All.Single(template => template.Id == id);

    private static IPlatform PlatformFor(string os) => os switch
    {
        "windows" => new WindowsPlatform(FakeEnvironment.Windows().Build()),
        "linux" => new LinuxPlatform(FakeEnvironment.Linux().Build()),
        "macos" => new MacOSPlatform(FakeEnvironment.MacOS().Build()),
        _ => throw new ArgumentOutOfRangeException(nameof(os), os, "Not one of the three systems."),
    };

    // Every mods folder a template names, with the system it is for.
    private static IEnumerable<(string Os, string Path)> ModsPaths(GameTemplate template)
    {
        if (template.Definition.Mods is { } mods)
        {
            yield return ("windows", mods.Path);
        }

        foreach (var (os, location) in template.Hints.ModsByOs)
        {
            yield return (os, location.Path);
        }
    }

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
    public void The_mods_folder_of_a_draft_is_never_inside_or_around_its_saves_folder()
    {
        var template = Template("7-days-to-die");

        foreach (var os in new[] { OsKind.Windows, OsKind.Linux, OsKind.MacOS })
        {
            var draft = template.ToDraft(os);

            Assert.Empty(CatalogValidator.Validate(draft with { Install = new InstallLocation("{userHome}/Games/7dtd") }));
        }
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Mods_folders_resolve_on_the_system_they_are_written_for(string id)
    {
        foreach (var (os, path) in ModsPaths(Template(id)))
        {
            Assert.Matches(@"^\{[A-Za-z]+\}", path);
            Assert.DoesNotContain("..", path);
            var resolved = Record.Exception(() => new PathTokenResolver(PlatformFor(os)).Resolve(path, "mods.path"));
            var issues = (resolved as ValidationException)?.Args["issues"] as IReadOnlyList<ValidationIssue>;

            Assert.True(resolved is null, $"'{path}' does not resolve on {os}: {issues?.FirstOrDefault()?.MessageId ?? resolved?.Message}");
        }
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void A_mods_folder_does_not_end_in_a_file_name_or_a_wildcard(string id)
    {
        foreach (var (os, path) in ModsPaths(Template(id)))
        {
            var last = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)[^1];

            Assert.DoesNotContain('*', path);
            Assert.DoesNotContain('?', path);
            Assert.True(Path.GetExtension(last).Length == 0, $"'{path}' ({os}) ends in a file name, not a folder.");
        }
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void A_mods_folder_is_not_one_of_the_platforms_broad_folders(string id)
    {
        foreach (var (os, path) in ModsPaths(Template(id)))
        {
            var platform = PlatformFor(os);
            var resolved = new PathTokenResolver(platform).Resolve(path, "mods.path");
            var separators = new[] { '/', '\\' };
            foreach (var entry in platform.Denylist([]).Entries)
            {
                var blocked = entry.Path.TrimEnd(separators);
                var here = resolved.TrimEnd(separators);
                var inside = entry.Tier == DenylistTier.Subtree
                    && here.StartsWith(blocked + (os == "windows" ? '\\' : '/'), platform.PathComparison)
                    && !(entry.AllowedSubRoots ?? []).Any(root => here.StartsWith(root.TrimEnd(separators), platform.PathComparison));
                var equal = !entry.ChildrenOnly && string.Equals(here, blocked, platform.PathComparison);

                Assert.False(equal || inside, $"'{path}' ({os}) is {resolved}, which SavePoint will not track: {entry.Reason}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Per_system_mods_use_only_the_three_system_names(string id)
    {
        Assert.All(Template(id).Hints.ModsByOs.Keys, key => Assert.Contains(key, new[] { "windows", "linux", "macos" }));
    }
}
