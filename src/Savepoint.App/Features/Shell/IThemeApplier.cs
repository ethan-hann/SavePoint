using Savepoint.App.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Shell;

/// <summary>Applies the saved theme choice to the running app; the shell calls it when settings load or change.</summary>
public interface IThemeApplier
{
    /// <summary>Switches the mode and accent.</summary>
    void Apply(ThemePreference preference, AccentColorId accent);

    /// <summary>
    /// Draws the main window, and the dialogs, popups, menus and tooltips it opens, at a UI scale in percent on top of the operating
    /// system's own scaling. The crash window and the already-running notice are minimal on purpose and stay at the system's size.
    /// </summary>
    void ApplyScale(int percent);
}

/// <summary>The real applier, backed by the application's <see cref="ThemeService"/>.</summary>
public sealed class ThemeServiceApplier : IThemeApplier
{
    private readonly ThemeService _service;

    /// <param name="service">The application's theme service.</param>
    public ThemeServiceApplier(ThemeService service)
    {
        _service = service;
    }

    /// <inheritdoc />
    public void Apply(ThemePreference preference, AccentColorId accent) => _service.Apply(preference, accent);

    /// <inheritdoc />
    public void ApplyScale(int percent) => _service.SetUiScale(percent);
}
