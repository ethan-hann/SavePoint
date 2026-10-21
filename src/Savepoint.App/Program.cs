using Avalonia;
using Savepoint.App.Theme.Fonts;

namespace Savepoint.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also used by the headless test host and the designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseSavepointFonts()
            .LogToTrace();
}
