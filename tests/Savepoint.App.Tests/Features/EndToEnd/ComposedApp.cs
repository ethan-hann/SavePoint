using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.History;
using Savepoint.App.Tests.Features.Settings;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.EndToEnd;

// The whole app as AppComposition builds it (real shell, Games, History and Settings screens, launch flow and editor) over the fake backend.
// Only the edges are replaced: the clock moves when the test moves it and, unless a test passes a dispatcher, events run on the calling thread. Without a view, dialogs answer
// from a script; with one, the window shows the real views and the real dialog host and a test drives the keyboard.
internal sealed class ComposedApp : IDisposable
{
    private readonly ThemeHost? _themeHost;
    private readonly FakeDialogs? _dialogs;
    private readonly RecordingThemeApplier? _theme;

    private ComposedApp(FakeBackend backend, FakeDialogs? dialogs, ManualTimeProvider clock, UiFormatter text, RecordingThemeApplier? theme, MainWindowViewModel main, MainView? view, ThemeHost? themeHost)
    {
        Backend = backend;
        _dialogs = dialogs;
        Clock = clock;
        Text = text;
        _theme = theme;
        Main = main;
        View = view;
        _themeHost = themeHost;
    }

    public FakeBackend Backend { get; }

    // The scripted dialogs of an app without a view; with one, the real dialog host is Main.Dialogs.
    public FakeDialogs Dialogs => _dialogs ?? throw new InvalidOperationException("The app runs with its real dialog host.");

    public ManualTimeProvider Clock { get; }

    public UiFormatter Text { get; }

    public RecordingThemeApplier Theme => _theme ?? throw new InvalidOperationException("The app applies themes for real.");

    public MainWindowViewModel Main { get; }

    public MainView? View { get; }

    public Window Window => _themeHost?.Window ?? throw new InvalidOperationException("The app was started without a view.");

    public ShellViewModel Shell => Main.Shell ?? throw new InvalidOperationException("The shell has not appeared.");

    public ToastHostViewModel Toasts => Main.Toasts;

    // The screens the shell built, each shown once at the start so a test holds on to the one it needs.
    public GamesScreenViewModel Games { get; private set; } = null!;

    public HistoryScreenViewModel History { get; private set; } = null!;

    public SettingsScreenViewModel Settings { get; private set; } = null!;

    public IReadOnlyList<string> ToastTexts => [.. Toasts.Toasts.Select(toast => toast.Text)];

    public static string RenderFolder(string area) => Path.Combine(ThemeTestSupport.RepoRoot(), "render-output", area);

    // The instant dispatcher unless a test passes another; the real one queues idle work and posts from other threads, as the app does.
    public static async Task<ComposedApp> StartAsync(FakeBackend? backend = null, Func<ISettingsApi, ISettingsApi>? settings = null, IUiDispatcher? dispatcher = null)
    {
        backend ??= FakeBackend.Sample();
        var (services, clock, text) = Parts(backend, settings);
        var dialogs = new FakeDialogs();
        var theme = new RecordingThemeApplier();
        var main = AppComposition.Create(services, text, theme, () => null, dispatcher ?? new ImmediateUiDispatcher(), clock, dialogs, SettingsHost(noticesShipped: true));
        await main.InitializeAsync();
        return Visited(new ComposedApp(backend, dialogs, clock, text, theme, main, null, null));
    }

    // The app in a window under the real styles, with the real dialog host; the theme and layout come from the backend's settings.
    public static async Task<ComposedApp> StartWithViewAsync(
        FakeBackend? backend = null,
        ThemePreference theme = ThemePreference.Dark,
        ShellLayout layout = ShellLayout.Sidebar,
        double width = 1040,
        double height = 660,
        bool visitScreens = true,
        bool noticesShipped = true,
        IUiDispatcher? dispatcher = null)
    {
        backend ??= new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Theme = theme, Layout = layout }));
        var (services, clock, text) = Parts(backend, null);
        var application = (App)Application.Current!;
        var main = AppComposition.Create(services, text, new ThemeServiceApplier(application.Theme), () => null, dispatcher ?? new ImmediateUiDispatcher(), clock, settingsEnvironment: SettingsHost(noticesShipped));
        var view = new MainView { DataContext = main };
        var host = new ThemeHost(view, theme, width: width, height: height);
        await main.InitializeAsync();
        var app = new ComposedApp(backend, null, clock, text, null, main, view, host);
        if (visitScreens)
        {
            Visited(app);
        }

        app.Settle();
        return app;
    }

    // Without a view there is no UI thread to run; the app's events already run on the calling thread.
    public void Settle()
    {
        if (View is not null)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    // Transitions (the nav pills fade over 150 ms) follow the render clock, which only runs when ticked: let it run past the longest one.
    public void SettleAnimations()
    {
        if (View is null)
        {
            return;
        }

        for (var tick = 0; tick < 12; tick++)
        {
            Thread.Sleep(25);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // Shows a destination the way its nav item does and answers with its screen.
    public T Show<T>(NavDestination destination)
        where T : class, IScreen
    {
        Shell.Navigate(destination);
        Settle();
        return Assert.IsType<T>(Shell.CurrentScreen);
    }

    // The newest dialog of a kind the app has shown; a test answers it through the view model's own commands.
    public T Dialog<T>()
        where T : class, IDialogViewModel
    {
        var shown = (_dialogs?.Shown ?? [.. Main.Dialogs.Stack]).OfType<T>().ToList();
        Assert.NotEmpty(shown);
        return shown[^1];
    }

    public T Find<T>(string name)
        where T : Control =>
        All<T>().FirstOrDefault(control => control.Name == name) ?? throw new InvalidOperationException($"No {typeof(T).Name} named {name}.");

    public IEnumerable<T> All<T>()
        where T : Visual => (View ?? throw new InvalidOperationException("The app was started without a view.")).GetVisualDescendants().OfType<T>();

    public void Press(Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPress(key, modifiers, physical, null);
        Window.KeyRelease(key, modifiers, physical, null);
        Settle();
    }

    public InputElement? Focused => Window.FocusManager?.GetFocusedElement() as InputElement;

    public string Render(string area, string name) => RenderTo(Path.Combine(RenderFolder(area), name + ".png"));

    // For a picture no test reads: it is only drawn when a person asks for the renders (SAVEPOINT_RENDER=1), so a gate run does not wait on it.
    public void RenderIfRequested(string area, string name)
    {
        if (RenderOutput.Requested)
        {
            Render(area, name);
        }
    }

    public string RenderTo(string path)
    {
        Settle();
        SettleAnimations();
        return ThemeTestSupport.SaveFrame(Window, path);
    }

    public void Dispose()
    {
        Main.Dispose();
        _themeHost?.Dispose();
    }

    // Settings reads the notices file from beside the executable; a test says whether one shipped, so it never depends on the build output.
    private static SettingsEnvironment SettingsHost(bool noticesShipped) =>
        new(new RecordingFileRevealer(), () => noticesShipped ? @"C:\SavePoint\THIRD-PARTY-NOTICES.md" : null, "1.0.0");

    private static (AppServices Services, ManualTimeProvider Clock, UiFormatter Text) Parts(FakeBackend backend, Func<ISettingsApi, ISettingsApi>? settings)
    {
        var clock = new ManualTimeProvider(ScenarioBuilder.DefaultNow);
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), clock, "windows", HistoryRig.SampleZone);
        var services = new AppServices(
            backend.Games,
            backend.Profiles,
            backend.Launch,
            settings is null ? backend.Settings : settings(backend.Settings),
            backend.History,
            backend.Jobs,
            backend.AppStatus,
            backend.Artwork);
        return (services, clock, text);
    }

    private static ComposedApp Visited(ComposedApp app)
    {
        Assert.NotNull(app.Main.Shell);
        app.Games = app.Show<GamesScreenViewModel>(NavDestination.Games);
        app.History = app.Show<HistoryScreenViewModel>(NavDestination.History);
        app.Settings = app.Show<SettingsScreenViewModel>(NavDestination.Settings);
        app.Shell.Navigate(NavDestination.Games);
        return app;
    }
}
