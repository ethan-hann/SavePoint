using System.Text.RegularExpressions;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Catalog;

/// <summary>
/// Checks game, profile, and settings values and normalizes them for storage. <c>Validate</c> reports every problem at once;
/// <c>RequireValid</c> is the throwing form and returns the normalized value to store.
/// </summary>
/// <remarks>
/// Only the shape of each field is checked. Path tokens, whether a path is absolute, and path safety belong to the <c>Paths</c>
/// namespace, and whether a folder exists is the service's check.
/// </remarks>
public static partial class CatalogValidator
{
    /// <summary>
    /// Returns the game as it will be stored: names and paths trimmed, blank optional text turned into null, and the ignore
    /// patterns and mod hints trimmed with blanks and repeats dropped (first occurrence kept, order kept). Checks nothing.
    /// </summary>
    /// <param name="game">The game to normalize.</param>
    /// <exception cref="ArgumentNullException"><paramref name="game"/> is null.</exception>
    public static GameDefinition Normalize(GameDefinition game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var launch = game.Launch;
        return game with
        {
            Name = Trim(game.Name),
            Install = new InstallLocation(Trim(game.Install.Path)),
            External = game.External is null ? null : game.External with { Path = Trim(game.External.Path) },
            Mods = game.Mods is null ? null : game.Mods with { Path = Trim(game.Mods.Path) },
            Launch = launch with
            {
                ExePath = TrimToNull(launch.ExePath),
                Uri = TrimToNull(launch.Uri),
                ProcessNameForExitDetection = TrimToNull(launch.ProcessNameForExitDetection),
            },
            IgnorePaths = CleanEntries(game.IgnorePaths),
            InstallIgnorePaths = CleanEntries(game.InstallIgnorePaths),
            ModPathHints = CleanEntries(game.ModPathHints),
        };
    }

    /// <summary>Returns the profile as it will be stored: the name trimmed, and a blank version override turned into null. Checks nothing.</summary>
    /// <param name="profile">The profile to normalize.</param>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    public static Profile Normalize(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile with { Name = Trim(profile.Name), VersionOverride = TrimToNull(profile.VersionOverride) };
    }

    /// <summary>Returns the settings as they will be stored: the storage folder trimmed. Checks nothing.</summary>
    /// <param name="settings">The settings to normalize.</param>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
    public static AppSettings Normalize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings with { StorageRoot = Trim(settings.StorageRoot) };
    }

    /// <summary>
    /// Normalizes the game, then lists every problem found; empty means valid. Checks the schema version, the id's shape, the
    /// name, the install and external paths (present, not nested), the mods path (present when given, nested with neither), the external role, the Steam app id (positive when given), and
    /// the launch method with its program path (relative, no <c>..</c>) or link (has a scheme).
    /// </summary>
    /// <param name="game">The game to check.</param>
    /// <returns>The problems, each with the field's JSON path; empty when the game is valid.</returns>
    public static IReadOnlyList<ValidationIssue> Validate(GameDefinition game) => Check(Normalize(game));

    /// <summary>
    /// Normalizes the profile, then lists every problem found: a missing id, game id, name, or branch name, or a branch name
    /// with a bad shape.
    /// </summary>
    /// <param name="profile">The profile to check.</param>
    /// <returns>The problems, each with the field's JSON path; empty when the profile is valid.</returns>
    public static IReadOnlyList<ValidationIssue> Validate(Profile profile) => Check(Normalize(profile));

    /// <summary>
    /// Normalizes the settings, then lists every problem found: a missing storage folder, a theme, accent, or layout that is not
    /// a defined value, or a locale that is not shaped like a language tag.
    /// </summary>
    /// <param name="settings">The settings to check.</param>
    /// <returns>The problems, each with the field's JSON path; empty when the settings are valid.</returns>
    public static IReadOnlyList<ValidationIssue> Validate(AppSettings settings) => Check(Normalize(settings));

    /// <summary>Normalizes the game and returns it, or throws when it has any problem.</summary>
    /// <param name="game">The game to check.</param>
    /// <returns>The normalized game, ready to store.</returns>
    /// <exception cref="ValidationException">The game has problems; the exception lists every one.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="game"/> is null.</exception>
    public static GameDefinition RequireValid(GameDefinition game)
    {
        var normalized = Normalize(game);
        var issues = Check(normalized);
        return issues.Count == 0 ? normalized : throw new ValidationException(issues);
    }

    /// <summary>Normalizes the profile and returns it, or throws when it has any problem.</summary>
    /// <param name="profile">The profile to check.</param>
    /// <returns>The normalized profile, ready to store.</returns>
    /// <exception cref="ValidationException">The profile has problems; the exception lists every one.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    public static Profile RequireValid(Profile profile)
    {
        var normalized = Normalize(profile);
        var issues = Check(normalized);
        return issues.Count == 0 ? normalized : throw new ValidationException(issues);
    }

    /// <summary>Normalizes the settings and returns them, or throws when they have any problem.</summary>
    /// <param name="settings">The settings to check.</param>
    /// <returns>The normalized settings, ready to store.</returns>
    /// <exception cref="ValidationException">The settings have problems; the exception lists every one.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
    public static AppSettings RequireValid(AppSettings settings)
    {
        var normalized = Normalize(settings);
        var issues = Check(normalized);
        return issues.Count == 0 ? normalized : throw new ValidationException(issues);
    }

    // The id names a folder under the storage root, so it can never carry a separator or a dot segment.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex GameIdPattern();

    /// <summary>True when the text is a game id the catalog accepts: letters, digits, dashes and underscores, starting with a letter or digit.</summary>
    /// <param name="id">The text to judge.</param>
    internal static bool IsValidGameId(string id) => GameIdPattern().IsMatch(id);

    // \z, not $: in .NET $ also matches before a trailing newline.
    [GeneratedRegex(@"^[A-Za-z0-9/_-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex BranchNamePattern();

    // Only the shape is checked: which locales exist follows the catalog files shipped next to en.json, which Core cannot see.
    [GeneratedRegex(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*\z", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTagPattern();

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

        if (game.SteamAppId is <= 0)
        {
            issues.Add(new ValidationIssue("steamAppId", ValidationMessageIds.GameSteamAppIdInvalid));
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

        if (game.Mods is { } mods)
        {
            if (mods.Path.Length == 0)
            {
                issues.Add(new ValidationIssue("mods.path", ValidationMessageIds.GameModsPathRequired));
            }
            else if ((game.Install.Path.Length > 0 && PathsNested(game.Install.Path, mods.Path))
                || (game.External is { Path.Length: > 0 } saves && PathsNested(saves.Path, mods.Path)))
            {
                // The issue goes on the mods field whichever folder it meets, so the player is sent to the one that was added last.
                issues.Add(new ValidationIssue("mods.path", ValidationMessageIds.GamePathsNested));
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

        if (!LanguageTagPattern().IsMatch(settings.Locale))
        {
            issues.Add(new ValidationIssue("locale", ValidationMessageIds.SettingsLocaleInvalid));
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
