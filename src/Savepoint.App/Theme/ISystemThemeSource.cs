namespace Savepoint.App.Theme;

// The operating system's light/dark choice, behind a seam so tests can simulate the OS changing it.
public interface ISystemThemeSource
{
    bool IsDark { get; }

    event EventHandler? Changed;
}
