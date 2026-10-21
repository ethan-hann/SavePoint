using System.Text.RegularExpressions;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Catalog;

// Validate reports every problem at once; RequireValid is the throwing form and returns the normalized value to store.
public static partial class CatalogValidator
{
    private const string _supportedLocale = "en";

    public static GameDefinition Normalize(GameDefinition game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var launch = game.Launch;
        return game with
        {
            Name = Trim(game.Name),
            Install = new InstallLocation(Trim(game.Install.Path)),
            External = game.External is null ? null : game.External with { Path = Trim(game.External.Path) },
            Launch = launch with
            {
                ExePath = TrimToNull(launch.ExePath),
                Uri = TrimToNull(launch.Uri),
                ProcessNameForExitDetection = TrimToNull(launch.ProcessNameForExitDetection),
            },
            IgnorePaths = CleanEntries(game.IgnorePaths),
            ModPathHints = CleanEntries(game.ModPathHints),
        };
    }

    public static Profile Normalize(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile with { Name = Trim(profile.Name), VersionOverride = TrimToNull(profile.VersionOverride) };
    }

    public static AppSettings Normalize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings with { StorageRoot = Trim(settings.StorageRoot) };
    }

    public static IReadOnlyList<ValidationIssue> Validate(GameDefinition game) => Check(Normalize(game));

    public static IReadOnlyList<ValidationIssue> Validate(Profile profile) => Check(Normalize(profile));

    public static IReadOnlyList<ValidationIssue> Validate(AppSettings settings) => Check(Normalize(settings));

    public static GameDefinition RequireValid(GameDefinition game)
    {
        var normalized = Normalize(game);
        var issues = Check(normalized);
        return issues.Count == 0 ? normalized : throw new ValidationException(issues);
    }

    public static Profile RequireValid(Profile profile)
    {
        var normalized = Normalize(profile);
        var issues = Check(normalized);
        return issues.Count == 0 ? normalized : throw new ValidationException(issues);
    }

    public static AppSettings RequireValid(AppSettings settings)
    {
        var normalized = Normalize(settings);
        var issues = Check(normalized);
        return issues.Count == 0 ? normalized : throw new ValidationException(issues);
    }

    // The id names a folder under the storage root, so it can never carry a separator or a dot segment.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex GameIdPattern();

    // \z, not $: in .NET $ also matches before a trailing newline.
    [GeneratedRegex(@"^[A-Za-z0-9/_-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex BranchNamePattern();

    // At least two scheme characters, so a drive letter such as C: is never taken for a scheme.
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]+:\S", RegexOptions.CultureInvariant)]
    private static partial Regex UriWithSchemePattern();

    private static List<ValidationIssue> Check(GameDefinition game)
    {
        var issues = new List<ValidationIssue>();

        if (game.SchemaVersion != GameDefinition.CurrentSchemaVersion)
        {
            issues.Add(new ValidationIssue("schemaVersion", ValidationMessageIds.GameSchemaVersionUnsupported));
        }

        if (string.IsNullOrWhiteSpace(game.Id))
        {
            issues.Add(new ValidationIssue("id", ValidationMessageIds.GameIdRequired));
        }
        else if (!GameIdPattern().IsMatch(game.Id))
        {
            issues.Add(new ValidationIssue("id", ValidationMessageIds.GameIdInvalid));
        }

        if (game.Name.Length == 0)
        {
            issues.Add(new ValidationIssue("name", ValidationMessageIds.GameNameRequired));
        }

        if (game.Install.Path.Length == 0)
        {
            issues.Add(new ValidationIssue("install.path", ValidationMessageIds.GameInstallPathRequired));
        }

        if (game.External is { } external)
        {
            if (external.Path.Length == 0)
            {
                issues.Add(new ValidationIssue("external.path", ValidationMessageIds.GameExternalPathRequired));
            }

            if (!Enum.IsDefined(external.Role))
            {
                issues.Add(new ValidationIssue("external.role", ValidationMessageIds.GameExternalRoleInvalid));
            }

            if (game.Install.Path.Length > 0 && external.Path.Length > 0 && PathsNested(game.Install.Path, external.Path))
            {
                issues.Add(new ValidationIssue("external.path", ValidationMessageIds.GamePathsNested));
            }
        }

        CheckLaunch(game.Launch, issues);
        return issues;
    }

    private static void CheckLaunch(LaunchConfig launch, List<ValidationIssue> issues)
    {
        if (!Enum.IsDefined(launch.Method))
        {
            issues.Add(new ValidationIssue("launch.method", ValidationMessageIds.LaunchMethodInvalid));
        }
        else if (launch.Method == LaunchMethod.Exe)
        {
            if (launch.ExePath is not { } exePath)
            {
                issues.Add(new ValidationIssue("launch.exePath", ValidationMessageIds.LaunchExePathRequired));
            }
            else if (IsAbsoluteExePath(exePath))
            {
                issues.Add(new ValidationIssue("launch.exePath", ValidationMessageIds.LaunchExePathAbsolute));
            }
            else if (ClimbsOut(exePath))
            {
                issues.Add(new ValidationIssue("launch.exePath", ValidationMessageIds.LaunchExePathEscapes));
            }
        }
        else if (launch.Uri is not { } uri)
        {
            issues.Add(new ValidationIssue("launch.uri", ValidationMessageIds.LaunchUriRequired));
        }
        else if (!UriWithSchemePattern().IsMatch(uri))
        {
            issues.Add(new ValidationIssue("launch.uri", ValidationMessageIds.LaunchUriInvalid));
        }
    }

    private static List<ValidationIssue> Check(Profile profile)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            issues.Add(new ValidationIssue("id", ValidationMessageIds.ProfileIdRequired));
        }

        if (string.IsNullOrWhiteSpace(profile.GameId))
        {
            issues.Add(new ValidationIssue("gameId", ValidationMessageIds.ProfileGameIdRequired));
        }

        if (profile.Name.Length == 0)
        {
            issues.Add(new ValidationIssue("name", ValidationMessageIds.ProfileNameRequired));
        }

        if (string.IsNullOrEmpty(profile.BranchName))
        {
            issues.Add(new ValidationIssue("branchName", ValidationMessageIds.ProfileBranchNameRequired));
        }
        else if (!IsValidBranchName(profile.BranchName))
        {
            issues.Add(new ValidationIssue("branchName", ValidationMessageIds.ProfileBranchNameInvalid));
        }

        return issues;
    }

    private static List<ValidationIssue> Check(AppSettings settings)
    {
        var issues = new List<ValidationIssue>();

        if (settings.StorageRoot.Length == 0)
        {
            issues.Add(new ValidationIssue("storageRoot", ValidationMessageIds.SettingsStorageRootRequired));
        }

        if (!Enum.IsDefined(settings.Theme))
        {
            issues.Add(new ValidationIssue("theme", ValidationMessageIds.SettingsThemeInvalid));
        }

        if (!Enum.IsDefined(settings.AccentColorId))
        {
            issues.Add(new ValidationIssue("accentColorId", ValidationMessageIds.SettingsAccentColorInvalid));
        }

        if (settings.Locale != _supportedLocale)
        {
            issues.Add(new ValidationIssue("locale", ValidationMessageIds.SettingsLocaleUnsupported));
        }

        if (!Enum.IsDefined(settings.Layout))
        {
            issues.Add(new ValidationIssue("layout", ValidationMessageIds.SettingsLayoutInvalid));
        }

        return issues;
    }

    // A branch name also ends up as a git argument, so one that starts with "-" could read as an option.
    private static bool IsValidBranchName(string branchName) =>
        BranchNamePattern().IsMatch(branchName)
        && !branchName.StartsWith('/')
        && !branchName.StartsWith('-')
        && !branchName.EndsWith('/')
        && !branchName.Contains("//", StringComparison.Ordinal);

    // Lexical on purpose: a Windows definition has to validate the same on every OS.
    private static bool IsAbsoluteExePath(string exePath) =>
        exePath[0] is '/' or '\\'
        || (exePath.Length >= 2 && char.IsAsciiLetter(exePath[0]) && exePath[1] == ':');

    // Any ".." is refused, not only net-escaping ones: a symlinked folder makes "a/.." land somewhere else.
    private static bool ClimbsOut(string exePath) =>
        exePath.Split('/', '\\').Contains("..");

    // Component-wise so C:/Games and C:/GamesX are separate while C:/Games and C:/Games/Saves nest.
    private static bool PathsNested(string left, string right)
    {
        var a = Components(left);
        var b = Components(right);
        var shared = Math.Min(a.Length, b.Length);
        for (var i = 0; i < shared; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    private static string[] Components(string path) =>
        path.Replace('\\', '/').ToLowerInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static string Trim(string? value) => value?.Trim() ?? "";

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static List<string> CleanEntries(IReadOnlyList<string> entries)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cleaned = new List<string>();
        foreach (var entry in entries)
        {
            var trimmed = Trim(entry);
            if (trimmed.Length > 0 && seen.Add(trimmed))
            {
                cleaned.Add(trimmed);
            }
        }

        return cleaned;
    }
}
