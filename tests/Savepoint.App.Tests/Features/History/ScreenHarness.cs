using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Savepoint.App.Composition;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.History;

// The whole window over the fake backend with only the screen under test registered, so a render shows it the way the shell does.
// Times read in the sample scenario's zone and "this week" runs from the fake clock; the launch flow is scripted so buttons are enabled.
internal sealed class ScreenHarness : IDisposable
{
    private readonly ThemeHost _host;

    private ScreenHarness(FakeBackend backend, MainWindowViewModel main, ThemeHost host, MainView view, ScriptedLaunchFlow flow, FakeDialogs dialogs, UiFormatter text, string outputFolder)
    {
        Backend = backend;
        Main = main;
        _host = host;
        View = view;
        Flow = flow;
        FakeDialogs = dialogs;
        Text = text;
        OutputFolder = outputFolder;
    }

    public FakeBackend Backend { get; }

    public MainWindowViewModel Main { get; }

    public MainView View { get; }

    public ScriptedLaunchFlow Flow { get; }

    public FakeDialogs FakeDialogs { get; }

    public UiFormatter Text { get; }

    public string OutputFolder { get; }

    public Window Window => _host.Window;

    public ShellViewModel Shell => Main.Shell ?? throw new InvalidOperationException("The shell has not appeared.");

    // factories build the screens under test from the shared context; area names the render-output subfolder.
    public static async Task<ScreenHarness> StartAsync(
        string area,
        Func<FeatureContext, IReadOnlyList<IScreenFactory>> factories,
        NavDestination destination,
        FakeBackend? backend = null,
        ThemePreference theme = ThemePreference.Dark,
        double width = 1040,
        double height = 660,
        bool realDialogs = false,
        IUiDispatcher? dispatcher = null)
    {
        backend ??= new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Theme = theme }));
        var text = HistoryRig.FormatterFor(backend);
        var app = (App)Application.Current!;
        var services = new AppServices(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus, backend.Artwork);

        // The immediate one runs idle work inline; a test that needs work to wait for the current input to finish passes the real one.
        dispatcher ??= new ImmediateUiDispatcher();
        var toasts = new ToastHostViewModel(new ManualTimeProvider(), dispatcher);
        var dialogStack = new DialogHostViewModel();
        var fakeDialogs = new FakeDialogs();
        IDialogs dialogs = realDialogs ? new AvaloniaDialogs(dialogStack, text, () => null) : fakeDialogs;
        var context = new FeatureContext(services, dialogs, text, toasts, dispatcher);
        var flow = new ScriptedLaunchFlow();
        var main = new MainWindowViewModel(
            new StartupViewModel(services.AppStatus, text, new ImmediateUiDispatcher()),
            () => new ShellViewModel(new ShellDependencies(
                backend.Games,
                backend.Profiles,
                backend.Settings,
                context.SettingsWriter,
                backend.Jobs,
                text,
                dispatcher,
                dialogs,
                toasts,
                new ThemeServiceApplier(app.Theme),
                StandInScreens.Complete(factories(context)),
                new ArtworkProvider(backend.Artwork, dispatcher),
                flow)),
            dialogStack,
            toasts);
        var view = new MainView { DataContext = main };
        var host = new ThemeHost(view, theme, width: width, height: height);
        var harness = new ScreenHarness(backend, main, host, view, flow, fakeDialogs, text, Path.Combine(ThemeTestSupport.RepoRoot(), "render-output", area));
        await main.InitializeAsync();
        harness.Shell.Navigate(destination);
        harness.Settle();
        return harness;
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

    public IEnumerable<T> All<T>()
        where T : Visual => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(View).OfType<T>();

    // Texts a person would read: visible, not faded out.
    public IEnumerable<string?> VisibleTexts() =>
        All<TextBlock>().Where(block => block.IsEffectivelyVisible && block.Opacity > 0).Select(block => block.Text);

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
