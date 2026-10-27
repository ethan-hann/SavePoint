using System.ComponentModel;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Navigation;
using Savepoint.App.Features.Common.Preferences;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Settings;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

// A shell wired to the fake backend with the real Games screen in its place, plus the launch flow and the editor as recordings.
internal sealed class GamesRig
{
    private GamesRig(FakeBackend backend, IProfilesApi? profiles, IUiDispatcher? dispatcher, Func<UiFormatter, IDialogs>? dialogs, TimeProvider? clock)
    {
        Backend = backend;
        Watcher = new FakeFolderWatcher();
        Window = new FakeWindowActivity();
        Text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), backend.Clock, "windows", TimeZoneInfo.Utc);
        Dialogs = new FakeDialogs();
        UiDialogs = dialogs?.Invoke(Text) ?? Dialogs;
        Toasts = new ToastHostViewModel(new ManualTimeProvider(), new ImmediateUiDispatcher());
        LaunchFlow = new ConfigurableLaunchFlow();
        Editor = new RecordingEditorFactory();
        Revealer = new RecordingFileRevealer();
        var ui = dispatcher ?? new ImmediateUiDispatcher();
        var factory = new CapturingScreenFactory(new GamesScreenFactory(new GamesDependencies(
            backend.Games,
            profiles ?? backend.Profiles,
            backend.Jobs,
            UiDialogs,
            Text,
            Toasts,
            ui,
            clock ?? backend.Clock,
            Editor,
            Revealer,
            Watcher,
            Window)));
        Factory = factory;
        Shell = new ShellViewModel(new ShellDependencies(
            backend.Games,
            backend.Profiles,
            backend.Settings,
            new SettingsWriter(backend.Settings),
            backend.Jobs,
            Text,
            ui,
            UiDialogs,
            Toasts,
            new RecordingThemeApplier(),
            StandInScreens.Complete([factory]),
            new ArtworkProvider(backend.Artwork, ui),
            LaunchFlow,
            Clock: clock));
    }

    public FakeBackend Backend { get; }

    public UiFormatter Text { get; }

    // What tests script and inspect; it is also what the screen uses unless a real dialog host was plugged in.
    public FakeDialogs Dialogs { get; }

    public IDialogs UiDialogs { get; }

    public ToastHostViewModel Toasts { get; }

    public ConfigurableLaunchFlow LaunchFlow { get; }

    public RecordingEditorFactory Editor { get; }

    public RecordingFileRevealer Revealer { get; }

    public FakeFolderWatcher Watcher { get; }

    public FakeWindowActivity Window { get; }

    public CapturingScreenFactory Factory { get; }

    public ShellViewModel Shell { get; }

    public GamesScreenViewModel Screen => Factory.Screen ?? throw new InvalidOperationException("The shell has not built the Games screen yet.");

    public static async Task<GamesRig> StartAsync(
        FakeBackend? backend = null,
        IProfilesApi? profiles = null,
        IUiDispatcher? dispatcher = null,
        Func<UiFormatter, IDialogs>? dialogs = null,
        TimeProvider? clock = null)
    {
        var rig = new GamesRig(backend ?? FakeBackend.Sample(), profiles, dispatcher, dialogs, clock);
        await rig.Shell.InitializeAsync();
        await rig.Screen.ReloadAsync();
        return rig;
    }

    public IReadOnlyList<string> ToastTexts => [.. Toasts.Toasts.Select(toast => toast.Text)];
}

internal static class TaskExtensions
{
    // A dialog that never answers should fail a test, not hang the run.
    public static Task Soon(this Task task) => task.WaitAsync(TimeSpan.FromSeconds(5));

    public static Task<T> Soon<T>(this Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(5));
}

internal sealed class CapturingScreenFactory(IScreenFactory inner) : IScreenFactory
{
    public NavDestination Destination => inner.Destination;

    public GamesScreenViewModel? Screen { get; private set; }

    public IScreen Create(IShellContext shell)
    {
        var screen = (GamesScreenViewModel)inner.Create(shell);
        Screen = screen;
        return screen;
    }
}

internal sealed class ConfigurableLaunchFlow : ILaunchFlowHost
{
    private readonly HashSet<string> _active = [];
    private readonly Dictionary<string, object?> _inline = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<string>? InlineContentChanged;

    public event EventHandler<AppSettings>? SettingsSaved;

    public event EventHandler<string>? EditGameRequested;

    public List<(string GameId, LaunchTarget Target)> Begun { get; } = [];

    public Task BeginAsync(string gameId, LaunchTarget target)
    {
        Begun.Add((gameId, target));
        return Task.CompletedTask;
    }

    public List<(string GameId, LaunchTarget Target)> BegunSwitches { get; } = [];

    public Task BeginSwitchAsync(string gameId, LaunchTarget target)
    {
        BegunSwitches.Add((gameId, target));
        return Task.CompletedTask;
    }

    public bool IsActive(string gameId) => _active.Contains(gameId);

    public object? InlineContentFor(string gameId) => _inline.GetValueOrDefault(gameId);

    public void RaiseEditGameRequested(string gameId) => EditGameRequested?.Invoke(this, gameId);

    public void RaiseSettingsSaved(AppSettings settings) => SettingsSaved?.Invoke(this, settings);

    public void SetActive(string gameId, bool active)
    {
        if (active)
        {
            _active.Add(gameId);
        }
        else
        {
            _active.Remove(gameId);
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Active"));
    }

    public void SetInline(string gameId, object? content)
    {
        _inline[gameId] = content;
        InlineContentChanged?.Invoke(this, gameId);
    }
}

internal sealed record EditorPane(string Kind, string? GameId, GameDefinition? Prefill);

internal sealed class RecordingEditorFactory : IGameEditorFactory
{
    public List<EditorPane> Created { get; } = [];

    public object CreateForExisting(IShellContext shell, string gameId)
    {
        var pane = new EditorPane("existing", gameId, null);
        Created.Add(pane);
        return pane;
    }

    public object CreateForNew(IShellContext shell, GameDefinition? prefill)
    {
        var pane = new EditorPane("new", null, prefill);
        Created.Add(pane);
        return pane;
    }
}
