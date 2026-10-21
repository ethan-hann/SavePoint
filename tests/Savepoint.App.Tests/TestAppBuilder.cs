using Avalonia;
using Avalonia.Headless;
using Savepoint.App.Localization;
using Savepoint.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Savepoint.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        // Tests run in Release too, where a missing key would otherwise pass silently.
        LocalizationCatalog.ThrowOnMissingKeyByDefault = true;

        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
