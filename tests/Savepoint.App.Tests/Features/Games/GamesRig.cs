using System.ComponentModel;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Navigation;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

// A shell wired to the fake backend with the real Games screen in its place, plus the launch flow and the editor as recordings.
internal sealed class GamesRig
{
    private GamesRig(FakeBackend backend, bool withLaunchFlow, bool withEditor, IProfilesApi? profiles, IUiDispatcher? dispatcher)
    {
        Backend = backend;
        Text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), backend.Clock, "windows", TimeZoneInfo.Utc);
        Dialogs = new FakeDialogs();
        Toasts = new ToastHostViewModel(new ManualTimeProvider(), new ImmediateUiDispatcher());
        LaunchFlow = withLaunchFlow ? new ConfigurableLaunchFlow() : null;
        Editor = withEditor ? new RecordingEditorFactory() : null;
        var ui = dispatcher ?? new ImmediateUiDispatcher();
        var factory = new CapturingScreenFactory(new GamesScreenFactory(new GamesDependencies(
            backend.Games,
            profiles ?? backend.Profiles,
            backend.Jobs,
            Dialogs,
            Text,
            Toasts,
            ui,
            backend.Clock,
            Editor)));
        Factory = factory;
        Shell = new ShellViewModel(new ShellDependencies(
            backend.Games,
            backend.Profiles,
            backend.Settings,
            backend.Jobs,
            Text,
            ui,
            Dialogs,
            Toasts,
            new RecordingThemeApplier(),
            [factory],
            LaunchFlow));
    }

    public FakeBackend Backend { get; }

    public UiFormatter Text { get; }

    public FakeDialogs Dialogs { get; }

    public ToastHostViewModel Toasts { get; }

    public ConfigurableLaunchFlow? LaunchFlow { get; }

    public RecordingEditorFactory? Editor { get; }

    public CapturingScreenFactory Factory { get; }

    public ShellViewModel Shell { get; }

    public GamesScreenViewModel Screen => Factory.Screen ?? throw new InvalidOperationException("The shell has not built the Games screen yet.");

    public static async Task<GamesRig> StartAsync(
        FakeBackend? backend = null,
        bool withLaunchFlow = true,
        bool withEditor = true,
        IProfilesApi? profiles = null,
        IUiDispatcher? dispatcher = null)
    {
        var rig = new GamesRig(backend ?? FakeBackend.Mockup(), withLaunchFlow, withEditor, profiles, dispatcher);
        await rig.Shell.InitializeAsync();
        await rig.Screen.ReloadAsync();
        return rig;
    }

    public IReadOnlyList<string> ToastTexts => [.. Toasts.Toasts.Select(toast => toast.Text)];
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

    public List<(string GameId, LaunchTarget Target)> Begun { get; } = [];

    public Task BeginAsync(string gameId, LaunchTarget target)
    {
        Begun.Add((gameId, target));
        return Task.CompletedTask;
    }

    public bool IsActive(string gameId) => _active.Contains(gameId);

    public object? InlineContentFor(string gameId) => _inline.GetValueOrDefault(gameId);

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
