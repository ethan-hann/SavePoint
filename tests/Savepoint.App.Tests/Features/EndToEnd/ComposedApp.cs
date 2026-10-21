using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.History;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.EndToEnd;

// The whole app as AppComposition builds it (real shell, Games, History and Settings screens, launch flow and editor) over the fake backend.
// Only the edges are replaced: dialogs answer from a script, the clock moves when the test moves it, and events run on the calling thread.
internal sealed class ComposedApp : IDisposable
{
    private ComposedApp(FakeBackend backend, FakeDialogs dialogs, ManualTimeProvider clock, UiFormatter text, RecordingThemeApplier theme, MainWindowViewModel main)
    {
        Backend = backend;
        Dialogs = dialogs;
        Clock = clock;
        Text = text;
        Theme = theme;
        Main = main;
    }

    public FakeBackend Backend { get; }

    public FakeDialogs Dialogs { get; }

    public ManualTimeProvider Clock { get; }

    public UiFormatter Text { get; }

    public RecordingThemeApplier Theme { get; }

    public MainWindowViewModel Main { get; }

    public ShellViewModel Shell => Main.Shell ?? throw new InvalidOperationException("The shell has not appeared.");

    public ToastHostViewModel Toasts => Main.Toasts;

    // The Games screen is where the app starts; the others are built by the shell too, and first read when shown.
    public GamesScreenViewModel Games => Show<GamesScreenViewModel>(NavDestination.Games);

    public IReadOnlyList<string> ToastTexts => [.. Toasts.Toasts.Select(toast => toast.Text)];

    public static async Task<ComposedApp> StartAsync(FakeBackend? backend = null, Func<ISettingsApi, ISettingsApi>? settings = null)
    {
        backend ??= FakeBackend.Mockup();
        var clock = new ManualTimeProvider(ScenarioBuilder.DefaultNow);
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), clock, "windows", HistoryRig.MockupZone);
        var services = new AppServices(
            backend.Games,
            backend.Profiles,
            backend.Launch,
            settings is null ? backend.Settings : settings(backend.Settings),
            backend.History,
            backend.Jobs,
            backend.AppStatus);
        var dialogs = new FakeDialogs();
        var theme = new RecordingThemeApplier();
        var main = AppComposition.Create(services, text, theme, () => null, new ImmediateUiDispatcher(), clock, dialogs);
        await main.InitializeAsync();
        var app = new ComposedApp(backend, dialogs, clock, text, theme, main);
        Assert.NotNull(main.Shell);
        return app;
    }

    // Shows a destination the way its nav item does and answers with its screen.
    public T Show<T>(NavDestination destination)
        where T : class, IScreen
    {
        Shell.Navigate(destination);
        return Assert.IsType<T>(Shell.CurrentScreen);
    }

    // The newest dialog of a kind the app has shown; a test answers it through the view model's own commands.
    public T Dialog<T>()
        where T : class, IDialogViewModel
    {
        var shown = Dialogs.Shown.OfType<T>().ToList();
        Assert.NotEmpty(shown);
        return shown[^1];
    }

    public void Dispose() => Main.Shell?.Dispose();
}
