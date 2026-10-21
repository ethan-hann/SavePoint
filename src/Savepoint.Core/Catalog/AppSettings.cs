namespace Savepoint.Core.Catalog;

public sealed record AppSettings(
    string StorageRoot = "",
    ThemePreference Theme = ThemePreference.System,
    AccentColorId AccentColorId = AccentColorId.Blue,
    string Locale = "en",
    ShellLayout Layout = ShellLayout.Sidebar,
    bool ConfirmSessionCapture = true,
    bool SkipConfirmWhenOnlySaveChanges = false,
    DateTimeOffset? LastOptimizedAt = null)
{
    // Settings are only written once the user changes something, so first run starts from these.
    public static AppSettings Defaults(string defaultStorageRoot) => new(StorageRoot: defaultStorageRoot);
}
