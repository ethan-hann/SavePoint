namespace Savepoint.Core.Catalog;

/// <summary>The user's app-wide settings, stored in <c>settings.json</c>.</summary>
/// <param name="StorageRoot">
/// Absolute folder that holds every game's repositories. Required. The default is a local, never-synced folder (D13.23). It
/// changes only by moving storage, never by saving settings (D23).
/// </param>
/// <param name="Theme">Light, dark, or follow the OS.</param>
/// <param name="AccentColorId">The accent preset the palette is generated from.</param>
/// <param name="Locale">
/// A language tag such as <c>en</c>. Only its shape is checked, because the available locales are the catalog files shipped next
/// to <c>en.json</c> (D20).
/// </param>
/// <param name="Layout">How the main window arranges navigation and the game list.</param>
/// <param name="ConfirmSessionCapture">
/// True to show the post-play dialog when a game exits. False skips it: nothing is captured and the session completes as skipped.
/// </param>
/// <param name="SkipConfirmWhenOnlySaveChanges">
/// True to start a game without the launch dialog when only the saves folder would change and nothing is unsaved.
/// </param>
/// <param name="LastOptimizedAt">
/// When storage optimization last ran, or null if it never did. Written by optimization, never by saving settings (D23).
/// </param>
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
    /// <summary>
    /// The settings for a first run. They are written only once the user changes something, so every setting but the storage
    /// folder is its default until then.
    /// </summary>
    /// <param name="defaultStorageRoot">The platform's default storage folder (D13.23).</param>
    public static AppSettings Defaults(string defaultStorageRoot) => new(StorageRoot: defaultStorageRoot);
}
