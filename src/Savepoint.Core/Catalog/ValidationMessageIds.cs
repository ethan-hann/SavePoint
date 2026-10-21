namespace Savepoint.Core.Catalog;

// Catalog keys for field-level validation copy; the UI turns each into a sentence.
public static class ValidationMessageIds
{
    public const string GameSchemaVersionUnsupported = "validation.game.schemaVersionUnsupported";
    public const string GameIdRequired = "validation.game.idRequired";
    public const string GameIdInvalid = "validation.game.idInvalid";
    public const string GameNameRequired = "validation.game.nameRequired";
    public const string GameInstallPathRequired = "validation.game.installPathRequired";
    public const string GameExternalPathRequired = "validation.game.externalPathRequired";
    public const string GameExternalRoleInvalid = "validation.game.externalRoleInvalid";
    public const string GamePathsNested = "validation.game.pathsNested";

    public const string LaunchMethodInvalid = "validation.launch.methodInvalid";
    public const string LaunchExePathRequired = "validation.launch.exePathRequired";
    public const string LaunchExePathAbsolute = "validation.launch.exePathAbsolute";
    public const string LaunchExePathEscapes = "validation.launch.exePathEscapes";
    public const string LaunchUriRequired = "validation.launch.uriRequired";
    public const string LaunchUriInvalid = "validation.launch.uriInvalid";

    public const string ProfileIdRequired = "validation.profile.idRequired";
    public const string ProfileGameIdRequired = "validation.profile.gameIdRequired";
    public const string ProfileNameRequired = "validation.profile.nameRequired";
    public const string ProfileBranchNameRequired = "validation.profile.branchNameRequired";
    public const string ProfileBranchNameInvalid = "validation.profile.branchNameInvalid";

    public const string SettingsStorageRootRequired = "validation.settings.storageRootRequired";
    public const string SettingsThemeInvalid = "validation.settings.themeInvalid";
    public const string SettingsAccentColorInvalid = "validation.settings.accentColorInvalid";
    public const string SettingsLayoutInvalid = "validation.settings.layoutInvalid";
    public const string SettingsLocaleUnsupported = "validation.settings.localeUnsupported";
}
