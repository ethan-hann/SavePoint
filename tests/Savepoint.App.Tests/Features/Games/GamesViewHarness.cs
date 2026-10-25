using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

// The whole window over the fake backend with the real Games screen in the shell and the real dialog host above it,
// shown under the application styles; renders go to render-output/games/.
internal sealed class GamesViewHarness : IDisposable
{
    private readonly ThemeHost _host;

    private GamesViewHarness(GamesRig rig, MainWindowViewModel main, DialogHostViewModel dialogs, ThemeHost host, MainView view)
    {
        Rig = rig;
        Main = main;
        Dialogs = dialogs;
        _host = host;
        View = view;
    }

    public GamesRig Rig { get; }

    public MainWindowViewModel Main { get; }

    public DialogHostViewModel Dialogs { get; }

    public MainView View { get; }

    public Window Window => _host.Window;

    public static string OutputFolder => Path.Combine(ThemeTestSupport.RepoRoot(), "render-output", "games");

    public static async Task<GamesViewHarness> StartAsync(
        FakeBackend? backend = null,
        ThemePreference theme = ThemePreference.Dark,
        ShellLayout layout = ShellLayout.Sidebar,
        double width = 1040,
        double height = 660)
    {
        backend ??= new FakeBackend(ScenarioBuilder.Mockup().WithSettings(MockupScenario.Settings with { Theme = theme, Layout = layout }));
        var dialogStack = new DialogHostViewModel();
        var rig = await GamesRig.StartAsync(backend, dialogs: text => new AvaloniaDialogs(dialogStack, text, () => null));
        var main = new MainWindowViewModel(new StartupViewModel(backend.AppStatus, rig.Text), () => rig.Shell, dialogStack, rig.Toasts);
        var view = new MainView { DataContext = main };
        var host = new ThemeHost(view, theme, width: width, height: height);
        var harness = new GamesViewHarness(rig, main, dialogStack, host, view);
        await main.InitializeAsync();
        harness.Settle();
        return harness;
    }

    public GamesScreenView Screen => All<GamesScreenView>().Single();

    public void Settle() => Dispatcher.UIThread.RunJobs();

    public T Find<T>(string name)
        where T : Control => FindIn<T>(View, name);

    // Names inside templates and nested views are not reachable by FindControl, so the visual tree is searched.
    public static T FindIn<T>(Control root, string name)
        where T : Control
    {
        var found = root.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);
        return found ?? throw new InvalidOperationException($"No {typeof(T).Name} named {name}.");
    }

    public IEnumerable<T> All<T>()
        where T : Visual => View.GetVisualDescendants().OfType<T>();

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
