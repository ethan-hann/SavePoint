using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Savepoint.App.Localization;
using Savepoint.App.Theme;
using Savepoint.App.Views;
using Savepoint.Core.Catalog;

namespace Savepoint.App;

public partial class App : Application
{
    /// <summary>
    /// Owns the application's color resources. Call <see cref="ThemeService.Apply"/> from the UI thread when the saved
    /// theme or accent changes, and set <see cref="ThemeService.ReduceMotion"/> from the system accessibility setting.
    /// </summary>
    public ThemeService Theme { get; private set; } = null!;

    public override void Initialize()
    {
        // Must be set before any XAML that uses the Loc extension is loaded.
        LocalizationCatalog.Current = LocalizationCatalog.LoadEmbedded("en");
        AvaloniaXamlLoader.Load(this);

        // Settings are not read yet, so this starts from the catalog defaults; the shell re-applies the saved choice.
        Theme = ThemeService.CreateForApplication(this);
        Theme.Apply(ThemePreference.System, AccentColorId.Blue);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
