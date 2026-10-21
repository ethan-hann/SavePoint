using Avalonia.Platform;
using Avalonia.Threading;

namespace Savepoint.App.Theme;

/// <summary>
/// Follows the operating system through Avalonia's platform settings. The platform can report a change from a
/// watcher thread, so <see cref="Changed"/> is always raised on the UI thread. Without platform settings it reports
/// light and never raises.
/// </summary>
public sealed class PlatformSystemThemeSource : ISystemThemeSource, IDisposable
{
    private readonly IPlatformSettings? _settings;

    /// <param name="settings">The application's platform settings, or null when none are available.</param>
    public PlatformSystemThemeSource(IPlatformSettings? settings)
    {
        _settings = settings;
        IsDark = settings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
        if (settings is not null)
        {
            settings.ColorValuesChanged += OnColorValuesChanged;
        }
    }

    /// <inheritdoc />
    public bool IsDark { get; private set; }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Stops listening to the platform.</summary>
    public void Dispose()
    {
        if (_settings is not null)
        {
            _settings.ColorValuesChanged -= OnColorValuesChanged;
        }
    }

    private void OnColorValuesChanged(object? sender, PlatformColorValues values)
    {
        IsDark = values.ThemeVariant == PlatformThemeVariant.Dark;
        if (Dispatcher.UIThread.CheckAccess())
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Changed?.Invoke(this, EventArgs.Empty));
        }
    }
}
