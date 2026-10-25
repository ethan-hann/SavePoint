using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Shell;

// The whole window content (startup, shell, overlays) over the fake backend, shown under the real application styles.
internal sealed class ShellViewHarness : IDisposable
{
    private readonly ThemeHost _host;

    private ShellViewHarness(FakeBackend backend, MainWindowViewModel main, ThemeHost host, MainView view)
    {
        Backend = backend;
        Main = main;
        _host = host;
        View = view;
    }

    public FakeBackend Backend { get; }

    public MainWindowViewModel Main { get; }

    public MainView View { get; }

    public Window Window => _host.Window;

    public ShellViewModel Shell => Main.Shell ?? throw new InvalidOperationException("The shell has not appeared.");

    public static string OutputFolder => Path.Combine(ThemeTestSupport.RepoRoot(), "render-output", "shell");

    public static async Task<ShellViewHarness> StartAsync(
        FakeBackend? backend = null,
        ThemePreference theme = ThemePreference.Dark,
        double width = 1040,
        double height = 660,
        bool realDialogs = false)
    {
        backend ??= FakeBackend.Sample();
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), backend.Clock, "windows", TimeZoneInfo.Utc);
        var app = (App)Application.Current!;
        var main = AppComposition.Create(
            new AppServices(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus, backend.Artwork),
            text,
            new ThemeServiceApplier(app.Theme),
            () => null,
            new ImmediateUiDispatcher(),
            new ManualTimeProvider(),
            realDialogs ? null : new FakeDialogs());
        var view = new MainView { DataContext = main };
        var host = new ThemeHost(view, theme, width: width, height: height);
        var harness = new ShellViewHarness(backend, main, host, view);
        await main.InitializeAsync();
        harness.Settle();
        return harness;
    }

    public static async Task<ShellViewHarness> LibraryLayoutAsync(ThemePreference theme = ThemePreference.Dark, double width = 1040, double height = 660)
    {
        var scenario = ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Layout = ShellLayout.Library, Theme = theme });
        return await StartAsync(new FakeBackend(scenario), theme, width, height);
    }

    public void Settle() => Dispatcher.UIThread.RunJobs();

    public T Find<T>(string name)
        where T : Control => FindIn<T>(View, name);

    public static T FindIn<T>(Control root, string name)
        where T : Control
    {
        var found = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>().FirstOrDefault(control => control.Name == name);
        return found ?? throw new InvalidOperationException($"No {typeof(T).Name} named {name}.");
    }

    // Names inside templates and nested views are not reachable by FindControl, so search the visual tree.
    public IEnumerable<T> All<T>()
        where T : Visual => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(View).OfType<T>();

    public string Render(string fileName)
    {
        Settle();
        return ThemeTestSupport.SaveFrame(Window, Path.Combine(OutputFolder, fileName + ".png"));
    }

    public void Dispose()
    {
        Main.Dispose();
        _host.Dispose();
    }
}
