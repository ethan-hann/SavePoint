using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Savepoint.App.Localization;
using Savepoint.App.Views;

namespace Savepoint.App;

public partial class App : Application
{
    public override void Initialize()
    {
        // Must be set before any XAML that uses the Loc extension is loaded.
        LocalizationCatalog.Current = LocalizationCatalog.LoadEmbedded("en");
        AvaloniaXamlLoader.Load(this);
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
