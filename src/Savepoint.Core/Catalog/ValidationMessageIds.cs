namespace Savepoint.Core.Catalog;

/// <summary>Catalog keys for field-level validation copy. Each key names one rule; the UI turns it into a sentence.</summary>
public static class ValidationMessageIds
{
    /// <summary>Reported at <c>schemaVersion</c>: the value is not <see cref="GameDefinition.CurrentSchemaVersion"/>.</summary>
    public const string GameSchemaVersionUnsupported = "validation.game.schemaVersionUnsupported";

    /// <summary>Reported at <c>id</c>: the game id is empty or blank.</summary>
    public const string GameIdRequired = "validation.game.idRequired";

    /// <summary>
    /// Reported at <c>id</c>: the game id has a character other than letters, digits, <c>_</c>, or <c>-</c>, or does not start with a
    /// letter or digit.
    /// </summary>
    public const string GameIdInvalid = "validation.game.idInvalid";

    /// <summary>Reported at <c>name</c>: the game name is empty after trimming.</summary>
    public const string GameNameRequired = "validation.game.nameRequired";

    /// <summary>Reported at <c>install.path</c>: the install folder is empty after trimming.</summary>
    public const string GameInstallPathRequired = "validation.game.installPathRequired";

    /// <summary>Reported at <c>external.path</c>: an external folder is present but its path is empty after trimming.</summary>
    public const string GameExternalPathRequired = "validation.game.externalPathRequired";

    /// <summary>Reported at <c>external.role</c>: the role is not a defined <see cref="ExternalRole"/>.</summary>
    public const string GameExternalRoleInvalid = "validation.game.externalRoleInvalid";

    /// <summary>Reported at <c>mods.path</c>: a mods folder is present but its path is empty after trimming.</summary>
    public const string GameModsPathRequired = "validation.game.modsPathRequired";

    /// <summary>
    /// Reported at <c>external.path</c> when the saves folder is the install folder, is inside it, or contains it, and at <c>mods.path</c>
    /// when the mods folder is the same as, inside, or around the install folder or the saves folder.
    /// </summary>
    public const string GamePathsNested = "validation.game.pathsNested";

    /// <summary>Reported at <c>steamAppId</c>: the Steam app id is zero or negative.</summary>
    public const string GameSteamAppIdInvalid = "validation.game.steamAppIdInvalid";

    /// <summary>Reported at <c>launch.method</c>: the value is not a defined <see cref="LaunchMethod"/>.</summary>
    public const string LaunchMethodInvalid = "validation.launch.methodInvalid";

    /// <summary>Reported at <c>launch.exePath</c>: the method is <see cref="LaunchMethod.Exe"/> and no program is named.</summary>
    public const string LaunchExePathRequired = "validation.launch.exePathRequired";

    /// <summary>
    /// Reported at <c>launch.exePath</c>: the program path is rooted (a leading slash, backslash, or drive letter) instead of relative
    /// to the install folder.
    /// </summary>
    public const string LaunchExePathAbsolute = "validation.launch.exePathAbsolute";

    /// <summary>Reported at <c>launch.exePath</c>: the program path has a <c>..</c> component and could leave the install folder (D13.11).</summary>
    public const string LaunchExePathEscapes = "validation.launch.exePathEscapes";

    /// <summary>Reported at <c>launch.uri</c>: the method is <see cref="LaunchMethod.Uri"/> and no link is given.</summary>
    public const string LaunchUriRequired = "validation.launch.uriRequired";

    /// <summary>Reported at <c>launch.uri</c>: the link does not start with a scheme such as <c>steam:</c>.</summary>
    public const string LaunchUriInvalid = "validation.launch.uriInvalid";

    /// <summary>Reported at <c>id</c>: the profile id is empty or blank.</summary>
    public const string ProfileIdRequired = "validation.profile.idRequired";

    /// <summary>Reported at <c>gameId</c>: the profile names no game.</summary>
    public const string ProfileGameIdRequired = "validation.profile.gameIdRequired";

    /// <summary>Reported at <c>name</c>: the profile name is empty after trimming.</summary>
    public const string ProfileNameRequired = "validation.profile.nameRequired";

    /// <summary>Reported at <c>startEmpty</c>: a new profile was asked to start with an empty install folder, or with a saves or mods folder the game does not have.</summary>
    public const string ProfileStartEmptyInvalid = "validation.profile.startEmptyInvalid";

    /// <summary>Reported at <c>startEmpty</c>: a folder asked to start empty holds files that no snapshot of any profile of the game has, so starting it empty could lose them.</summary>
    public const string ProfileStartEmptyUnsaved = "validation.profile.startEmptyUnsaved";

    /// <summary>Reported at <c>branchName</c>: the profile has no branch name.</summary>
    public const string ProfileBranchNameRequired = "validation.profile.branchNameRequired";

    /// <summary>
    /// Reported at <c>branchName</c>: the name has a character other than letters, digits, <c>/</c>, <c>_</c>, or <c>-</c>, starts with
    /// <c>/</c> or <c>-</c>, ends with <c>/</c>, or holds <c>//</c>.
    /// </summary>
    public const string ProfileBranchNameInvalid = "validation.profile.branchNameInvalid";

    /// <summary>Reported at <c>storageRoot</c>: the storage folder is empty after trimming.</summary>
    public const string SettingsStorageRootRequired = "validation.settings.storageRootRequired";

    /// <summary>Reported at <c>theme</c>: the value is not a defined <see cref="ThemePreference"/>.</summary>
    public const string SettingsThemeInvalid = "validation.settings.themeInvalid";

    /// <summary>Reported at <c>accentColorId</c>: the value is not a defined <see cref="AccentColorId"/>.</summary>
    public const string SettingsAccentColorInvalid = "validation.settings.accentColorInvalid";

    /// <summary>Reported at <c>layout</c>: the value is not a defined <see cref="ShellLayout"/>.</summary>
    public const string SettingsLayoutInvalid = "validation.settings.layoutInvalid";

    /// <summary>Reported at <c>locale</c>: the value is not shaped like a language tag, such as <c>en</c> or <c>pt-BR</c>.</summary>
    public const string SettingsLocaleInvalid = "validation.settings.localeInvalid";
}
