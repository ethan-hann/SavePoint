using Avalonia.Platform;
using Avalonia.Threading;

namespace Savepoint.App.Theme;

public sealed class PlatformSystemThemeSource : ISystemThemeSource, IDisposable
{
    private readonly IPlatformSettings? _settings;

    public PlatformSystemThemeSource(IPlatformSettings? settings)
    {
        _settings = settings;
        IsDark = settings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
        if (settings is not null)
        {
            settings.ColorValuesChanged += OnColorValuesChanged;
        }
    }

    public bool IsDark { get; private set; }

    public event EventHandler? Changed;

    public void Dispose()
    {
        if (_settings is not null)
        {
            _settings.ColorValuesChanged -= OnColorValuesChanged;
        }
    }

    // Platform backends may raise this from a watcher thread, so the change is handed to the UI thread.
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
