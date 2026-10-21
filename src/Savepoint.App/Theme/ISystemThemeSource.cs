namespace Savepoint.App.Theme;

/// <summary>
/// The operating system's light or dark choice, behind a seam so tests can simulate the system changing it.
/// </summary>
public interface ISystemThemeSource
{
    /// <summary>True when the system currently asks for dark. Read each time the theme is applied.</summary>
    bool IsDark { get; }

    /// <summary>Raised on the UI thread after <see cref="IsDark"/> may have changed.</summary>
    event EventHandler? Changed;
}
