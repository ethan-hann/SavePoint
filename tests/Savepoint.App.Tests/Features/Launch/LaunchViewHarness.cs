using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Launch;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Launch.Stub;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Launch;

// The real window content (shell, dialog overlay, toasts) with the launch flow wired to the fake backend and the Games screen replaced by a stub, shown under the real styles.
internal sealed class LaunchViewHarness : IDisposable
{
    private readonly ThemeHost _host;

    private LaunchViewHarness(LaunchRig rig, LaunchFlowViewModel flow, DialogHostViewModel dialogs, MainWindowViewModel main, ThemeHost host, MainView view)
    {
        Rig = rig;
        Flow = flow;
        Dialogs = dialogs;
        Main = main;
        _host = host;
        View = view;
    }

    public static string OutputFolder => Path.Combine(ThemeTestSupport.RepoRoot(), "render-output", "launch");

    public LaunchRig Rig { get; }

    public LaunchFlowViewModel Flow { get; }

    public DialogHostViewModel Dialogs { get; }

    public MainWindowViewModel Main { get; }

    public MainView View { get; }

    public FakeBackend Backend => Rig.Backend;

    public Window Window => _host.Window;

    public ShellViewModel Shell => Main.Shell ?? throw new InvalidOperationException("The shell has not appeared.");

    public static async Task<LaunchViewHarness> StartAsync(
        FakeBackend? backend = null,
        ThemePreference theme = ThemePreference.Dark,
        double width = 1040,
        double height = 660)
    {
        var scenario = backend ?? new FakeBackend(ScenarioBuilder.Mockup().WithSettings(MockupScenario.Settings with { Theme = theme }));
        var rig = new LaunchRig(scenario);
        var dialogs = new DialogHostViewModel();
        var real = new AvaloniaDialogs(dialogs, rig.Text, () => null);
        var immediate = new ImmediateUiDispatcher();
        var deps = rig.Deps with { Dialogs = real, Awaiter = rig.Awaiter };
        var flow = new LaunchFlowViewModel(deps);
        var app = (App)Application.Current!;
        var main = new MainWindowViewModel(
            new StartupViewModel(scenario.AppStatus, rig.Text),
            () => new ShellViewModel(new ShellDependencies(
                scenario.Games,
                scenario.Profiles,
                scenario.Settings,
                rig.Deps.SettingsWriter,
                scenario.Jobs,
                rig.Text,
                immediate,
                real,
                rig.Toasts,
                new ThemeServiceApplier(app.Theme),
                StandInScreens.Complete([new StubGamesScreenFactory()]),
                flow)),
            dialogs,
            rig.Toasts);
        var view = new MainView { DataContext = main };
        var host = new ThemeHost(view, theme, width: width, height: height);
        var harness = new LaunchViewHarness(rig, flow, dialogs, main, host, view);
        await main.InitializeAsync();
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

    public string Render(string fileName)
    {
        Settle();
        return ThemeTestSupport.SaveFrame(Window, Path.Combine(OutputFolder, fileName + ".png"));
    }

    public void Dispose()
    {
        Flow.Dispose();
        Rig.Dispose();
        _host.Dispose();
    }
}
