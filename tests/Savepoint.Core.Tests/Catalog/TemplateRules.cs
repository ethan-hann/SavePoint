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

// The rules every shipped template keeps, one method each. A rule returns a sentence for each problem it finds, so a test over every
// template names the template and the reason together, and a rule can also be tried on a deliberately bad template.
internal static partial class TemplateRules
{
    private static readonly string[] _systems = ["windows", "linux", "macos"];

    private static readonly HashSet<string> _fileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "sav", "save", "dat", "bin", "json", "xml", "ini", "cfg", "conf", "lua", "db", "sl2", "txt", "log", "bak", "data", "fsav",
        "b", "yml", "yaml", "plist", "sdt", "ess", "es3", "sqlite", "png", "jpg",
    };

    private static readonly (string DisplayName, string Squashed)[] _gameNames =
        [.. GameTemplates.All.Select(template => (template.DisplayName, Squash(template.DisplayName)))];

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex KebabCase();

    [GeneratedRegex(@"^[0-9]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex Digits();

    [GeneratedRegex(@"^\{[A-Za-z]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex PathToken();

    [GeneratedRegex(@"\b(commits?|branch(es)?|checkout|repos?|repositor(y|ies))\b|\bHEAD\b|—", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BannedInNotes();

    [GeneratedRegex(@"[^A-Za-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NotAlphanumeric();

    [GeneratedRegex(@"\(\d{4}\)", RegexOptions.CultureInvariant)]
    private static partial Regex Years();

    public static IPlatform PlatformFor(string os) => os switch
    {
        "windows" => new WindowsPlatform(FakeEnvironment.Windows().Build()),
        "linux" => new LinuxPlatform(FakeEnvironment.Linux().Build()),
        "macos" => new MacOSPlatform(FakeEnvironment.MacOS().Build()),
        _ => throw new ArgumentOutOfRangeException(nameof(os), os, "Not one of the three systems."),
    };

    public static string[] Segments(string path) => path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    // Letters and digits only, so "Crysis2Remastered" and "Crysis 2 Remastered" compare equal.
    public static string Squash(string text) => NotAlphanumeric().Replace(Years().Replace(text, string.Empty), string.Empty).ToLowerInvariant();

    // Every saves folder a template names, with the system it is for.
    public static IEnumerable<(string Os, string Path)> SavesPaths(GameTemplate template)
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

    // Every mods folder a template names, with the system it is for.
    public static IEnumerable<(string Os, string Path)> ModsPaths(GameTemplate template)
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

    // Runs a rule over templates and returns "id: problem" for every problem, so one failure lists them all.
    public static List<string> ProblemsIn(IEnumerable<GameTemplate> templates, Func<GameTemplate, IEnumerable<string>> rule) =>
        [.. templates.SelectMany(template => rule(template).Select(problem => $"{template.Id}: {problem}"))];

    public static void AssertShippedTemplatesKeep(Func<GameTemplate, IEnumerable<string>> rule)
    {
        var problems = ProblemsIn(GameTemplates.All, rule);

        Assert.True(problems.Count == 0, $"{problems.Count} problem(s):{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    // A template leaves the install folder for the player to pick, and is a complete game definition once they have.
    public static IEnumerable<string> ValidityProblems(GameTemplate template)
    {
        var definition = template.Definition;
        if (template.Id != definition.Id)
        {
            yield return $"the template id differs from the definition id '{definition.Id}'";
        }

        if (template.DisplayName != definition.Name || string.IsNullOrWhiteSpace(template.DisplayName))
        {
            yield return $"the display name '{template.DisplayName}' is blank or not the definition's name '{definition.Name}'";
        }

        if (definition.Install.Path.Length != 0)
        {
            yield return $"it names the install folder '{definition.Install.Path}', which no token can name";
        }

        var unpicked = CatalogValidator.Validate(definition).Select(issue => issue.Path).ToList();
        if (!unpicked.SequenceEqual(["install.path"]))
        {
            yield return $"before the player picks the install folder it reports [{string.Join(", ", unpicked)}] instead of only install.path";
        }

        var picked = definition with { Install = new InstallLocation(@"D:\Picked") };
        foreach (var issue in CatalogValidator.Validate(picked))
        {
            yield return $"after the player picks the install folder it still reports {issue.Path} ({issue.MessageId})";
        }

        if (!picked.Equals(CatalogValidator.Normalize(picked)))
        {
            yield return "validation would change it, so it is not stored in its plain form";
        }
    }

    public static IEnumerable<string> IdProblems(GameTemplate template)
    {
        if (!KebabCase().IsMatch(template.Id))
        {
            yield return "the id is not lowercase kebab-case";
        }

        if (template.Id.Length > 64)
        {
            yield return $"the id is {template.Id.Length} characters, over 64";
        }
    }

    public static IEnumerable<string> SteamIdProblems(GameTemplate template)
    {
        if (template.Definition.SteamAppId is <= 0)
        {
            yield return $"steamAppId {template.Definition.SteamAppId} is not positive";
        }

        foreach (var appId in template.Hints.SteamAppIds.Where(appId => appId <= 0))
        {
            yield return $"the Steam app id {appId} in the library hints is not positive";
        }
    }

    public static IEnumerable<string> StoreNameProblems(GameTemplate template)
    {
        foreach (var gog in template.Hints.GogIds.Where(gog => !Digits().IsMatch(gog)))
        {
            yield return $"the GOG id '{gog}' is not digits only";
        }

        foreach (var folder in template.Hints.SteamInstallDirs.Concat(template.Hints.GogFolders))
        {
            if (string.IsNullOrWhiteSpace(folder) || folder.Contains('/') || folder.Contains('\\') || folder.Contains("..") || folder != folder.Trim())
            {
                yield return $"the store folder name '{folder}' is not one plain name";
            }
        }
    }

    public static IEnumerable<string> SavesRoleProblems(GameTemplate template)
    {
        var locations = template.Hints.SavesByOs.Values.ToList();
        if (template.Definition.External is { } external)
        {
            locations.Add(external);
        }

        foreach (var location in locations.Where(location => location.Role != ExternalRole.Saves))
        {
            yield return $"'{location.Path}' has the role {location.Role}, not saves";
        }
    }

    // The field name is the one the app reports a bad path under ("external.path" or "mods.path").
    public static IEnumerable<string> ResolveProblems(IEnumerable<(string Os, string Path)> paths, string field)
    {
        foreach (var (os, path) in paths)
        {
            if (!PathToken().IsMatch(path))
            {
                yield return $"'{path}' ({os}) does not start with a path token";
            }

            if (path.Contains(".."))
            {
                yield return $"'{path}' ({os}) climbs out of its folder with ..";
            }

            var failure = Record.Exception(() => new PathTokenResolver(PlatformFor(os)).Resolve(path, field));
            if (failure is not null)
            {
                var issues = (failure as ValidationException)?.Args["issues"] as IReadOnlyList<ValidationIssue>;
                yield return $"'{path}' does not resolve on {os}: {issues?.FirstOrDefault()?.MessageId ?? failure.Message}";
            }
        }
    }

    // A saves folder is a folder: no wildcard, and no ending in a known file extension. A mods folder is stricter, and any extension counts.
    public static IEnumerable<string> FileNameProblems(IEnumerable<(string Os, string Path)> paths, bool anyExtensionIsAFile)
    {
        foreach (var (os, path) in paths)
        {
            if (path.Contains('*') || path.Contains('?'))
            {
                yield return $"'{path}' ({os}) has a wildcard";
            }

            var extension = Path.GetExtension(Segments(path)[^1]).TrimStart('.');
            if (extension.Length > 0 && (anyExtensionIsAFile || _fileExtensions.Contains(extension)))
            {
                yield return $"'{path}' ({os}) ends in a file name, not a folder";
            }
        }
    }

    public static IEnumerable<string> OtherGameProblems(GameTemplate template, IEnumerable<(string Os, string Path)> paths)
    {
        var own = Squash(template.DisplayName);
        var others = _gameNames
            .Where(other => other.DisplayName != template.DisplayName && other.Squashed.Length > 0 && !own.Contains(other.Squashed, StringComparison.Ordinal))
            .ToList();

        foreach (var (os, path) in paths)
        {
            var named = Segments(path).Select(Squash).ToHashSet();
            var clash = others.FirstOrDefault(other => named.Contains(other.Squashed));
            if (clash.DisplayName is not null)
            {
                yield return $"'{path}' ({os}) is a folder of '{clash.DisplayName}', not of '{template.DisplayName}'";
            }
        }
    }

    public static IEnumerable<string> BroadFolderProblems(IEnumerable<(string Os, string Path)> paths, string field)
    {
        var separators = new[] { '/', '\\' };
        foreach (var (os, path) in paths.Where(entry => _systems.Contains(entry.Os)))
        {
            var platform = PlatformFor(os);
            var resolved = new PathTokenResolver(platform).Resolve(path, field);
            var here = resolved.TrimEnd(separators);
            foreach (var entry in platform.Denylist([]).Entries)
            {
                var blocked = entry.Path.TrimEnd(separators);
                var inside = entry.Tier == DenylistTier.Subtree
                    && here.StartsWith(blocked + (os == "windows" ? '\\' : '/'), platform.PathComparison)
                    && !(entry.AllowedSubRoots ?? []).Any(root => here.StartsWith(root.TrimEnd(separators), platform.PathComparison));
                var equal = !entry.ChildrenOnly && string.Equals(here, blocked, platform.PathComparison);
                if (equal || inside)
                {
                    yield return $"'{path}' ({os}) is {resolved}, which SavePoint will not track: {entry.Reason}";
                }
            }
        }
    }

    public static IEnumerable<string> SystemNameProblems(IEnumerable<string> keys) =>
        keys.Where(key => !_systems.Contains(key)).Select(key => $"'{key}' is not one of windows, linux, macos");

    public static IEnumerable<string> NoteProblems(GameTemplate template)
    {
        if (template.Definition.Notes is { } notes && BannedInNotes().IsMatch(notes))
        {
            yield return $"the notes use a word the app never shows: {notes}";
        }
    }

    public static IEnumerable<string> LinkProblems(GameTemplate template)
    {
        var definition = template.Definition;
        if (definition.Launch.Method == LaunchMethod.Uri && definition.Launch.Uri != $"steam://rungameid/{definition.SteamAppId}")
        {
            yield return $"it starts through '{definition.Launch.Uri}', not the link of Steam game {definition.SteamAppId}";
        }
    }
}
