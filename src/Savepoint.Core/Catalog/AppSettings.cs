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
/// When the player last freed up space, or null if they never did. Written by that run only: the cleanups SavePoint starts by itself
/// (after a profile delete, or when snapshots pile up) leave it alone. Never written by saving settings.
/// </param>
/// <param name="LookUpArtworkOnline">
/// True to let SavePoint fetch a game's artwork from Steam's servers when it is not already on this computer (D30). False makes no
/// network request at all; artwork already on this computer is used either way. A settings file from before this setting existed
/// reads as true.
/// </param>
/// <param name="OlderChoiceHelpSeen">
/// True once the dialog after playing from an older snapshot has opened its "Which should I pick?" explanation for the player on
/// its own. It is kept per player, not per run, so the explanation opens by itself only the first time. A settings file from before
/// this setting existed reads as false.
/// </param>
/// <param name="DiscardHelpSeen">
/// True once a discard confirmation has added its one line of advice for the player. Kept per player; reads as false in a settings
/// file from before this setting existed.
/// </param>
/// <param name="ReplaceHelpSeen">
/// True once the Replace choice after playing has opened its "When should I replace?" explanation on its own. Kept per player;
/// reads as false in a settings file from before this setting existed.
/// </param>
/// <param name="TourSeen">
/// True once the first-run tour has started for the player. It starts once, after the first profile exists, and runs again only when
/// the player asks for it. Removing all of SavePoint's data resets it with everything else. A settings file from before this setting
/// existed reads as false.
/// </param>
public sealed record AppSettings(
    string StorageRoot = "",
    ThemePreference Theme = ThemePreference.System,
    AccentColorId AccentColorId = AccentColorId.Blue,
    string Locale = "en",
    ShellLayout Layout = ShellLayout.Sidebar,
    bool ConfirmSessionCapture = true,
    bool SkipConfirmWhenOnlySaveChanges = false,
    DateTimeOffset? LastOptimizedAt = null,
    bool LookUpArtworkOnline = true,
    bool OlderChoiceHelpSeen = false,
    bool DiscardHelpSeen = false,
    bool ReplaceHelpSeen = false,
    bool TourSeen = false)
{
    /// <summary>
    /// The settings for a first run. They are written only once the user changes something, so every setting but the storage
    /// folder is its default until then.
    /// </summary>
    /// <param name="defaultStorageRoot">The platform's default storage folder (D13.23).</param>
    public static AppSettings Defaults(string defaultStorageRoot) => new(StorageRoot: defaultStorageRoot);
}
