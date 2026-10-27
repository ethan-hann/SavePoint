using System.ComponentModel;
using Savepoint.App.Composition;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.History;

// The real shell with the History screen the production factory builds, over the fake backend; times read in the sample scenario's zone (UTC-5).
internal sealed class HistoryRig
{
    private HistoryRig(ShellRig shell, HistoryScreenViewModel screen, ScriptedLaunchFlow flow, FakeBackend backend, UiFormatter text, FakeDialogs dialogs)
    {
        ShellRig = shell;
        Screen = screen;
        Flow = flow;
        Backend = backend;
        Text = text;
        Dialogs = dialogs;
    }

    public ShellRig ShellRig { get; }

    public ShellViewModel Shell => ShellRig.Shell;

    public HistoryScreenViewModel Screen { get; }

    public ScriptedLaunchFlow Flow { get; }

    public FakeBackend Backend { get; }

    public UiFormatter Text { get; }

    public FakeDialogs Dialogs { get; }

    public static TimeZoneInfo SampleZone { get; } = TimeZoneInfo.CreateCustomTimeZone("sample", TimeSpan.FromHours(-5), "sample", "sample");

    public static UiFormatter FormatterFor(FakeBackend backend) =>
        new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), backend.Clock, "windows", SampleZone);

    // Starts the shell on Games and leaves History unvisited, so a test decides when the screen first shows.
    public static async Task<HistoryRig> StartAsync(
        FakeBackend? backend = null,
        Func<IHistoryApi, IHistoryApi>? history = null,
        TimeProvider? clock = null)
    {
        backend ??= FakeBackend.Sample();
        var flow = new ScriptedLaunchFlow();
        var text = FormatterFor(backend);
        var services = new AppServices(
            backend.Games,
            backend.Profiles,
            backend.Launch,
            backend.Settings,
            history is null ? backend.History : history(backend.History),
            backend.Jobs,
            backend.AppStatus,
            backend.Artwork);
        var dialogs = new FakeDialogs();
        var context = new FeatureContext(
            services,
            dialogs,
            text,
            new ToastHostViewModel(new ManualTimeProvider(), new ImmediateUiDispatcher()),
            new ImmediateUiDispatcher());
        var factory = new CapturingFactory(new HistoryScreenFactory(FeatureComposition.HistoryDependenciesOf(context, clock ?? backend.Clock)));
        var shell = await ShellRig.StartAsync(backend, screens: [factory], launchFlow: flow);
        return new HistoryRig(shell, (HistoryScreenViewModel)factory.Created!, flow, backend, text, dialogs);
    }

    // Shows the screen the way the nav item does.
    public void Show() => Shell.Navigate(NavDestination.History);

    public static async Task<HistoryRig> ShownAsync(FakeBackend? backend = null, TimeProvider? clock = null)
    {
        var rig = await StartAsync(backend, clock: clock);
        rig.Show();
        return rig;
    }

    public int HistoryReads => Backend.Calls.To<IHistoryApi>(nameof(IHistoryApi.GetAsync)).Count;

    // The shell keeps its screens private, so the factory hands the one it built to the rig.
    private sealed class CapturingFactory(IScreenFactory inner) : IScreenFactory
    {
        public NavDestination Destination => inner.Destination;

        public IScreen? Created { get; private set; }

        public IScreen Create(IShellContext shell) => Created = inner.Create(shell);
    }
}

// A launch flow a test drives: it records what began and says a game is busy until the test says otherwise.
internal sealed class ScriptedLaunchFlow : ILaunchFlowHost
{
    private readonly HashSet<string> _active = [];

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    public event EventHandler<string>? InlineContentChanged;

    public event EventHandler<AppSettings>? SettingsSaved
    {
        add { }
        remove { }
    }

    public event EventHandler<string>? EditGameRequested
    {
        add { }
        remove { }
    }

    public List<(string GameId, LaunchTarget Target)> Begun { get; } = [];

    // What starting the flow does to the game, for a test that needs it to take hold or fail.
    public Action<string>? OnBegin { get; set; }

    public Task BeginAsync(string gameId, LaunchTarget target)
    {
        Begun.Add((gameId, target));
        OnBegin?.Invoke(gameId);
        return Task.CompletedTask;
    }

    public List<(string GameId, LaunchTarget Target)> BegunSwitches { get; } = [];

    public Task BeginSwitchAsync(string gameId, LaunchTarget target)
    {
        BegunSwitches.Add((gameId, target));
        OnBegin?.Invoke(gameId);
        return Task.CompletedTask;
    }

    public bool IsActive(string gameId) => _active.Contains(gameId);

    public Dictionary<string, object?> Inline { get; } = [];

    public object? InlineContentFor(string gameId) => Inline.GetValueOrDefault(gameId);

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

        InlineContentChanged?.Invoke(this, gameId);
    }
}

// Holds every read until the test lets it go; the answer is computed when the call is made, so it reflects that call's arguments.
internal sealed class GatedHistoryApi : IHistoryApi
{
    private readonly IHistoryApi _inner;

    public GatedHistoryApi(IHistoryApi inner)
    {
        _inner = inner;
    }

    public bool Hold { get; set; }

    public List<PendingRead> Pending { get; } = [];

    public async Task<OperationResult<GameHistory>> GetAsync(string gameId, string? profileId = null, CancellationToken cancellationToken = default)
    {
        var result = await _inner.GetAsync(gameId, profileId, cancellationToken);
        if (Hold)
        {
            // Continuations run inline on Release, so a test sees the screen settle before its next line.
            var read = new PendingRead(gameId, profileId, new TaskCompletionSource());
            Pending.Add(read);
            await read.Gate.Task;
        }

        return result;
    }

    public Task<OperationResult<RecentlyRemoved>> GetRecentlyRemovedAsync(string gameId, CancellationToken cancellationToken = default) =>
        _inner.GetRecentlyRemovedAsync(gameId, cancellationToken);

    public sealed record PendingRead(string GameId, string? ProfileId, TaskCompletionSource Gate)
    {
        public void Release() => Gate.TrySetResult();
    }
}
