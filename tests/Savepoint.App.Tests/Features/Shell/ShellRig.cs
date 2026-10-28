using System.ComponentModel;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Preferences;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.History;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Shell;

// A shell wired to the fake backend with every collaborator recorded; events run inline unless a test passes a queueing dispatcher.
internal sealed class ShellRig
{
    private ShellRig(FakeBackend backend, IUiDispatcher dispatcher, IReadOnlyList<IScreenFactory> screens, ILaunchFlowHost? launchFlow, IArtworkProvider? artwork, TimeProvider? clock)
    {
        Backend = backend;
        Text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), backend.Clock, "windows", TimeZoneInfo.Utc);
        Dialogs = new FakeDialogs();
        Toasts = new ToastHostViewModel(new ManualTimeProvider(), dispatcher);
        Theme = new RecordingThemeApplier();
        Screens = screens;
        Shell = new ShellViewModel(new ShellDependencies(
            backend.Games,
            backend.Profiles,
            backend.Settings,
            new SettingsWriter(backend.Settings),
            backend.Jobs,
            Text,
            dispatcher,
            Dialogs,
            Toasts,
            Theme,
            screens,
            artwork ?? new ArtworkProvider(backend.Artwork, dispatcher),
            launchFlow ?? new ScriptedLaunchFlow(),
            Clock: clock));
    }

    public FakeBackend Backend { get; }

    public UiFormatter Text { get; }

    public FakeDialogs Dialogs { get; }

    public ToastHostViewModel Toasts { get; }

    public RecordingThemeApplier Theme { get; }

    public IReadOnlyList<IScreenFactory> Screens { get; }

    public ShellViewModel Shell { get; }

    public static ShellRig Create(
        FakeBackend? backend = null,
        IUiDispatcher? dispatcher = null,
        IReadOnlyList<IScreenFactory>? screens = null,
        ILaunchFlowHost? launchFlow = null,
        IArtworkProvider? artwork = null,
        bool standIns = true,
        TimeProvider? clock = null) =>
        new(backend ?? FakeBackend.Sample(), dispatcher ?? new ImmediateUiDispatcher(), standIns ? StandInScreens.Complete(screens ?? []) : screens ?? [], launchFlow, artwork, clock);

    public static async Task<ShellRig> StartAsync(
        FakeBackend? backend = null,
        IUiDispatcher? dispatcher = null,
        IReadOnlyList<IScreenFactory>? screens = null,
        ILaunchFlowHost? launchFlow = null,
        IArtworkProvider? artwork = null,
        bool standIns = true,
        TimeProvider? clock = null)
    {
        var rig = Create(backend, dispatcher, screens, launchFlow, artwork, standIns, clock);
        await rig.Shell.InitializeAsync();
        return rig;
    }

    public static async Task<ShellRig> LibraryLayoutAsync()
    {
        var scenario = ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Layout = ShellLayout.Library });
        return await StartAsync(new FakeBackend(scenario));
    }

    public IEnumerable<string> Changes(INotifyPropertyChanged source, Action act)
    {
        var names = new List<string>();
        void OnChanged(object? sender, PropertyChangedEventArgs e) => names.Add(e.PropertyName ?? string.Empty);
        source.PropertyChanged += OnChanged;
        act();
        source.PropertyChanged -= OnChanged;
        return names;
    }
}

internal sealed class RecordingThemeApplier : IThemeApplier
{
    public List<(ThemePreference Preference, AccentColorId Accent)> Applied { get; } = [];

    public List<int> Scales { get; } = [];

    public void Apply(ThemePreference preference, AccentColorId accent) => Applied.Add((preference, accent));

    public void ApplyScale(int percent) => Scales.Add(percent);

    public int LargestFittingScale { get; private set; } = 200;

    public event EventHandler? LargestFittingScaleChanged;

    // What the window's screen allows changed, as when the window is dragged to a smaller screen.
    public void ChangeLargestFittingScale(int percent)
    {
        LargestFittingScale = percent;
        LargestFittingScaleChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class RecordingScreen(NavDestination destination) : IScreen
{
    public NavDestination Destination { get; } = destination;

    public int ShownCount { get; private set; }

    public void OnShown() => ShownCount++;
}

internal sealed class RecordingScreenFactory(NavDestination destination) : IScreenFactory
{
    public NavDestination Destination { get; } = destination;

    public int Created { get; private set; }

    public IShellContext? Context { get; private set; }

    public RecordingScreen? Screen { get; private set; }

    public IScreen Create(IShellContext shell)
    {
        Created++;
        Context = shell;
        return Screen = new RecordingScreen(Destination);
    }
}

internal sealed class QueueingDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _queued = [];

    public int Pending => _queued.Count;

    public void Post(Action action) => _queued.Enqueue(action);

    public void RunAll()
    {
        while (_queued.TryDequeue(out var action))
        {
            action();
        }
    }
}

internal sealed class FakeLaunchFlow : ILaunchFlowHost
{
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    public List<(string GameId, LaunchTarget Target)> Begun { get; } = [];

    public Task BeginAsync(string gameId, LaunchTarget target)
    {
        Begun.Add((gameId, target));
        return Task.CompletedTask;
    }

    public Task BeginSwitchAsync(string gameId, LaunchTarget target) => Task.CompletedTask;

    public bool IsActive(string gameId) => false;

    public object? InlineContentFor(string gameId) => null;

    public event EventHandler<string>? InlineContentChanged
    {
        add { }
        remove { }
    }

    public event EventHandler<AppSettings>? SettingsSaved;

    public event EventHandler<string>? EditGameRequested
    {
        add { }
        remove { }
    }

    public int SettingsSavedSubscribers => SettingsSaved?.GetInvocationList().Length ?? 0;

    public void RaiseSettingsSaved(AppSettings settings) => SettingsSaved?.Invoke(this, settings);
}
