using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Editor;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Editor;

// The editor pane inside the real shell window over the fake backend, with the real dialog overlay above it.
internal sealed class EditorViewHarness : IDisposable
{
    private readonly ShellViewHarness _shell;
    private readonly GameEditorFactory _factory;

    private EditorViewHarness(ShellViewHarness shell, GameEditorFactory factory)
    {
        _shell = shell;
        _factory = factory;
    }

    public static string OutputFolder => Path.Combine(ThemeTestSupport.RepoRoot(), "render-output", "editor");

    public FakeBackend Backend => _shell.Backend;

    public Window Window => _shell.Window;

    public ShellViewModel Shell => _shell.Shell;

    public DialogHostViewModel Dialogs => _shell.Main.Dialogs;

    public static async Task<EditorViewHarness> StartAsync(FakeBackend? backend = null, ThemePreference theme = ThemePreference.Dark, string os = "windows", double height = 660, bool artwork = false)
    {
        backend ??= new FakeBackend(ScenarioBuilder.Mockup().WithSettings(MockupScenario.Settings with { Theme = theme }));
        var shell = await ShellViewHarness.StartAsync(backend, theme, height: height, realDialogs: true);
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), backend.Clock, os, TimeZoneInfo.Utc);
        var dialogs = new AvaloniaDialogs(shell.Main.Dialogs, text, () => null);
        return new EditorViewHarness(shell, new GameEditorFactory(backend.Games, dialogs, text, shell.Main.Toasts, artwork ? backend.Artwork : null));
    }

    public GameEditorViewModel Edit(string gameId = MockupScenario.SkyrimId) => Show((GameEditorViewModel)_factory.CreateForExisting(Shell, gameId));

    public GameEditorViewModel New(GameDefinition? prefill = null) => Show((GameEditorViewModel)_factory.CreateForNew(Shell, prefill));

    public GameEditorView View => _shell.All<GameEditorView>().Single();

    public T Find<T>(string name)
        where T : Control => ShellViewHarness.FindIn<T>(View, name);

    public IEnumerable<T> All<T>()
        where T : Visual => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(View).OfType<T>();

    public void Settle() => _shell.Settle();

    public string Render(string fileName)
    {
        Settle();
        var frame = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame was rendered.");
        Directory.CreateDirectory(OutputFolder);
        var path = Path.Combine(OutputFolder, fileName + ".png");
        frame.Save(path, new PngBitmapEncoderOptions());
        return path;
    }

    public void Dispose() => _shell.Dispose();

    private GameEditorViewModel Show(GameEditorViewModel editor)
    {
        Shell.ShowPane(editor);
        Dispatcher.UIThread.RunJobs();
        return editor;
    }
}
