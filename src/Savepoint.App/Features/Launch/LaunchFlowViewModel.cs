using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Launch;

/// <summary>
/// The shell-owned launch flow (D13.19): Play, the dialog, the switch, the running game, the post-play prompt and every
/// failure along the way. Its state is kept per game above all screens, layouts and selections, so changing any of them
/// never loses a running session or its post-play prompt. The Games screen starts it with <see cref="BeginAsync"/>,
/// shows <see cref="InlineContentFor"/> in the pane, and keeps Play unavailable while <see cref="IsActive"/>
/// is true; it can follow <see cref="ActiveGameIds"/> and <see cref="InlineContentChanged"/> to refresh both.
/// </summary>
/// <remarks>
/// What the inline content can be: a <see cref="LaunchBannerViewModel"/> while a game starts without a dialog, a
/// <see cref="RunningCardViewModel"/> while it runs, a <see cref="LaunchSavingViewModel"/> while the session's capture
/// runs, or an <c>ErrorCardViewModel</c> for a failure or a partial result. Each has a view found by name.
/// </remarks>
public sealed partial class LaunchFlowViewModel : ObservableObject, ILaunchFlowHost, IDisposable
{
    private readonly LaunchDependencies _deps;
    private readonly Dictionary<string, GameFlow> _flows = [];

    // The save jobs the flow follows to a card; read by the job indicator on the same thread the flow's events are handled on.
    private readonly HashSet<Guid> _followedSaves = [];
    private readonly Dictionary<string, string> _gameNames = [];
    private readonly Dictionary<(string GameId, string ProfileId), string> _profileNames = [];
    private IReadOnlyCollection<string> _activeGameIds = [];
    private bool _disposed;

    /// <param name="deps">The Api and formatting the flow uses.</param>
    public LaunchFlowViewModel(LaunchDependencies deps)
    {
        _deps = deps;

        // Subscribe first, then read the sessions that already exist, so a change in between is not lost.
        deps.Launch.SessionChanged += OnSessionChanged;
        deps.Jobs.JobChanged += OnJobChanged;
        foreach (var session in deps.Launch.ListSessions().Where(session => !IsEnded(session.State)))
        {
            var flow = FlowFor(session.GameId);
            Enqueue(flow, () => ApplyAsync(flow, session));
        }
    }

    /// <inheritdoc />
    public event EventHandler<string>? InlineContentChanged;

    /// <inheritdoc />
    public event EventHandler<string>? EditGameRequested;

    /// <inheritdoc />
    public event EventHandler<AppSettings>? SettingsSaved;

    /// <summary>The ids of the games the flow currently owns a session for, in order. Changes raise <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/>.</summary>
    public IReadOnlyCollection<string> ActiveGameIds => _activeGameIds;

    /// <inheritdoc />
    public bool ShowsOutcomeOf(Guid jobId) => _followedSaves.Contains(jobId);

    /// <inheritdoc />
    public bool IsActive(string gameId) => _flows.TryGetValue(gameId, out var flow) && IsOwned(flow);

    /// <inheritdoc />
    public object? InlineContentFor(string gameId) => _flows.TryGetValue(gameId, out var flow) ? flow.Inline : null;

    /// <summary>
    /// Starts the flow for a game: checks what starting the target would change, then either starts at once (showing the
    /// banner when nothing changes) or asks in the launch dialog. Does nothing while the game is already in the flow.
    /// Completes when the dialog has been answered and the start has been handed to the service; the session itself
    /// carries on through the flow's own state.
    /// </summary>
    public async Task BeginAsync(string gameId, LaunchTarget target)
    {
        var flow = FlowFor(gameId);
        if (IsOwned(flow))
        {
            return;
        }

        // The scan of both folders can take seconds on a large install, so the card that says so shows before it starts.
        using var checking = new CancellationTokenSource();
        flow.Stage = FlowStage.Previewing;
        flow.Target = target;

        // A start from an earlier session must not outlive its session: "Try again" on a card from this check may only repeat this check.
        flow.StartTarget = null;
        flow.Preview = null;
        SetInline(flow, new LaunchCheckingViewModel(_deps.Text, checking.Cancel));
        RaiseActivity();

        await RefreshNamesAsync(gameId);
        var preview = await _deps.Launch.GetPreviewAsync(gameId, target, checking.Token);
        if (!preview.Ok)
        {
            flow.Stage = FlowStage.Idle;

            // A person who pressed Cancel asked for this: no error card, just the table as it was.
            if (checking.IsCancellationRequested)
            {
                SetInline(flow, null);
            }
            else
            {
                ShowLaunchFailure(flow, preview.Error, beforeStart: true);
            }

            RaiseActivity();
            return;
        }

        var settings = await LoadSettingsAsync();
        var plan = preview.Value;

        // A file that cannot be read is the only thing wrong with a folder that has to be switched: say which, and offer to try again,
        // rather than a dialog whose choices (keep, discard) could not help.
        if (HeldFileIn(plan) is { } held)
        {
            flow.Stage = FlowStage.Idle;
            ShowHeldFile(flow, held);
            RaiseActivity();
            return;
        }

        if (plan.ShowBanner)
        {
            await StartAsync(flow, target, plan, viaBanner: true);
        }
        else if (plan.OnlySaveDiffers && settings.SkipConfirmWhenOnlySaveChanges && !HasUnsaved(plan))
        {
            await StartAsync(flow, target, plan, viaBanner: false);
        }
        else
        {
            await AskThenStartAsync(flow, target, plan, settings);
        }
    }

    /// <summary>Stops following the Api's events and ends the running card's refresh.</summary>
    public void Dispose()
    {
        _disposed = true;
        _deps.Launch.SessionChanged -= OnSessionChanged;
        _deps.Jobs.JobChanged -= OnJobChanged;
        foreach (var flow in _flows.Values)
        {
            (flow.Inline as IDisposable)?.Dispose();
        }
    }

    private static bool IsEnded(SessionState state) => state is SessionState.Completed or SessionState.Failed;

    private static bool IsOwned(GameFlow flow) =>
        flow.Stage is FlowStage.Previewing or FlowStage.Dialog or FlowStage.Starting
        || (flow.Session is { } session && !IsEnded(session.State));

    private static bool HasUnsaved(LaunchPreview preview) => preview.Install.Dirty || preview.External is { Dirty: true };

    // The held file the check stops on. Unsaved work or a half-finished switch comes first: the dialog settles those, and the file is met when the switch starts.
    private static ErrorInfo? HeldFileIn(LaunchPreview plan)
    {
        var folders = new[] { plan.Install, plan.External }.OfType<FolderLaunchState>().ToList();
        return folders.Any(folder => folder.Dirty || folder.UnfinishedSwitch)
            ? null
            : folders.Select(folder => folder.HeldBy).FirstOrDefault(held => held is not null);
    }

    private GameFlow FlowFor(string gameId)
    {
        if (!_flows.TryGetValue(gameId, out var flow))
        {
            _flows[gameId] = flow = new GameFlow(gameId);
        }

        return flow;
    }

    private async Task AskThenStartAsync(GameFlow flow, LaunchTarget target, LaunchPreview plan, AppSettings settings)
    {
        flow.Stage = FlowStage.Dialog;
        SetInline(flow, null);
        RaiseActivity();

        var dialog = new LaunchDialogViewModel(plan, GameName(flow.GameId), ProfileName(flow.GameId, plan.ProfileId), settings.SkipConfirmWhenOnlySaveChanges, _deps);
        dialog.SettingsSaved += (_, saved) => SettingsSaved?.Invoke(this, saved);
        var chosen = await _deps.Dialogs.ShowAsync(dialog);
        if (chosen is null)
        {
            flow.Stage = FlowStage.Idle;
            RaiseActivity();
            return;
        }

        await StartAsync(flow, chosen, plan, viaBanner: false);
    }

    private async Task StartAsync(GameFlow flow, LaunchTarget target, LaunchPreview plan, bool viaBanner)
    {
        // The session's own events may arrive before the call returns, so the flow is marked as starting first.
        flow.Stage = FlowStage.Starting;
        flow.StartTarget = target;
        flow.Preview = plan;
        flow.ViaBanner = viaBanner;
        SetInline(flow, viaBanner ? new LaunchBannerViewModel(plan, GameName(flow.GameId), ProfileName(flow.GameId, plan.ProfileId), _deps.Text, _deps.Dialogs) : null);
        RaiseActivity();

        var started = await _deps.Launch.StartAsync(flow.GameId, target);
        if (!started.Ok)
        {
            CloseProgress(flow);
            flow.Stage = FlowStage.Idle;
            ShowLaunchFailure(flow, started.Error);
            RaiseActivity();
        }
    }

    // Start the same switch again, the way "Try again" does; without a known start it re-enters the flow.
    private Task RestartAsync(GameFlow flow)
    {
        if (flow.StartTarget is { } target && flow.Preview is { } plan)
        {
            SetInline(flow, null);
            return StartAsync(flow, target, plan, viaBanner: false);
        }

        return BeginAsync(flow.GameId, flow.Target ?? throw new InvalidOperationException("The flow has no target to start."));
    }

    private void RaiseActivity()
    {
        var active = _flows.Values.Where(IsOwned).Select(flow => flow.GameId).Order(StringComparer.Ordinal).ToList();
        if (active.SequenceEqual(_activeGameIds))
        {
            return;
        }

        _activeGameIds = active;
        OnPropertyChanged(nameof(ActiveGameIds));
    }

    private void SetInline(GameFlow flow, object? content)
    {
        if (ReferenceEquals(flow.Inline, content))
        {
            return;
        }

        (flow.Inline as IDisposable)?.Dispose();
        flow.Inline = content;
        InlineContentChanged?.Invoke(this, flow.GameId);
    }

    private async Task RefreshNamesAsync(string gameId)
    {
        var games = await _deps.Games.ListAsync();
        if (games.Ok)
        {
            foreach (var game in games.Value)
            {
                _gameNames[game.Id] = game.Name;
            }
        }

        var profiles = await _deps.Profiles.ListAsync(gameId);
        if (profiles.Ok)
        {
            foreach (var profile in profiles.Value)
            {
                _profileNames[(profile.GameId, profile.Id)] = profile.Name;
            }
        }
    }

    private async Task EnsureNamesAsync(string gameId, string profileId)
    {
        if (!_gameNames.ContainsKey(gameId) || !_profileNames.ContainsKey((gameId, profileId)))
        {
            await RefreshNamesAsync(gameId);
        }
    }

    private string GameName(string gameId) =>
        _gameNames.TryGetValue(gameId, out var name) ? name : _deps.Text.Message("launch.thisGame");

    private string ProfileName(string gameId, string profileId) =>
        _profileNames.TryGetValue((gameId, profileId), out var name) ? name : _deps.Text.Message("launch.thisProfile");

    // A setting that cannot be read means the defaults, which ask rather than skip.
    private async Task<AppSettings> LoadSettingsAsync()
    {
        var settings = await _deps.Settings.GetAsync();
        return settings.Ok ? settings.Value : new AppSettings();
    }

    private void ShowError(ErrorInfo error) =>
        _deps.Toasts.Show(new ToastMessage(LaunchText.Reason(_deps.Text, error), ToastKind.Error));

    private enum FlowStage
    {
        Idle,
        Previewing,
        Dialog,
        Starting,
        Session,
    }

    // Everything the flow keeps for one game. Touched on the UI thread only.
    private sealed class GameFlow(string gameId)
    {
        public string GameId { get; } = gameId;

        public FlowStage Stage { get; set; }

        // What Play asked for, and what was actually started (the dialog's picker can change it).
        public LaunchTarget? Target { get; set; }

        public LaunchTarget? StartTarget { get; set; }

        public LaunchPreview? Preview { get; set; }

        public bool ViaBanner { get; set; }

        public LaunchSessionInfo? Session { get; set; }

        public object? Inline { get; set; }

        public SwitchProgressDialogViewModel? Progress { get; set; }

        public LaunchSavingViewModel? Saving { get; set; }

        public Guid? SavingJobId { get; set; }

        public long SavingRevision { get; set; }

        public Queue<Func<Task>> Work { get; } = new();

        public bool Pumping { get; set; }
    }
}
