using Avalonia.Controls;
using Avalonia.Threading;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Platform;
using Savepoint.App.Platform.Motion;
using Savepoint.App.Platform.Notifications;
using Savepoint.App.Theme;
using Savepoint.Core.Api;

namespace Savepoint.App.Hosting;

/// <summary>
/// What ties the running window to the operating system: a notification when a long job finishes while the window is not in
/// front (D26), the system's reduced-motion setting feeding the theme, and bringing the window forward when a second copy of
/// the app asks for it.
/// </summary>
public sealed class HostIntegration : IDisposable
{
    private readonly Window _window;
    private readonly WindowActivityTracker _activity;
    private readonly INotificationService _notifications;
    private readonly IGamesApi _games;
    private readonly IJobsApi _jobs;
    private readonly UiFormatter _text;
    private readonly ReducedMotionSource _motion;
    private readonly IDisposable _motionRefresh;
    private readonly SingleInstance? _single;
    private readonly ThemeService _theme;
    private readonly SessionAttentionRelay? _attention;
    private bool _disposed;

    private HostIntegration(Window window, ThemeService theme, AppServices services, UiFormatter text, SingleInstance? single, INotificationService? notifications, SessionAttentionRelay? attention)
    {
        _attention = attention;
        _window = window;
        _theme = theme;
        _games = services.Games;
        _jobs = services.Jobs;
        _text = text;
        _single = single;
        _activity = new WindowActivityTracker(window);
        _notifications = notifications ?? NotificationServices.Create(window);
        if (attention is not null)
        {
            attention.Target = new SessionAttention(_activity, _notifications, WindowFlashers.For(window), text);
        }

        _motion = ReducedMotionSource.CreateForCurrentOs();
        _motion.Changed += OnMotionChanged;
        _motionRefresh = _motion.RefreshOnActivation(_activity);
        _ = _motion.RefreshAsync();

        _jobs.JobChanged += OnJobChanged;
        if (_single is not null)
        {
            _single.ActivationRequested += OnActivationRequested;
        }
    }

    /// <summary>
    /// Starts the integration. Call it on the UI thread once the main window exists. It lives until <see cref="Dispose"/>.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="theme">Receives the reduced-motion setting.</param>
    /// <param name="services">The Api whose finished jobs raise notifications.</param>
    /// <param name="text">Formats the notification text.</param>
    /// <param name="single">The single-instance guard whose activation requests bring the window forward; null when there is none.</param>
    /// <param name="attention">Receives the real session-end handler (taskbar flash and notification) while the integration lives; null for none.</param>
    public static HostIntegration Attach(Window window, ThemeService theme, AppServices services, UiFormatter text, SingleInstance? single, SessionAttentionRelay? attention = null) =>
        new(window, theme, services, text, single, null, attention);

    // Lets a test see what would be shown without touching the real notification area.
    internal static HostIntegration Attach(Window window, ThemeService theme, AppServices services, UiFormatter text, INotificationService notifications) =>
        new(window, theme, services, text, null, notifications, null);

    /// <summary>Stops listening and gives up the notification area icon.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_attention is not null)
        {
            _attention.Target = NullSessionAttention.Instance;
        }

        _jobs.JobChanged -= OnJobChanged;
        if (_single is not null)
        {
            _single.ActivationRequested -= OnActivationRequested;
        }

        _motion.Changed -= OnMotionChanged;
        _motionRefresh.Dispose();
        _activity.Dispose();
    }

    private void OnMotionChanged(object? sender, EventArgs e) => _theme.ReduceMotion = _motion.IsReduced;

    // Job events arrive on a worker thread; the window state is read from the tracker's copy, and the words are made on the UI thread.
    private void OnJobChanged(object? sender, JobSnapshot job)
    {
        if (job.Status == JobStatus.Running || !NotificationPolicy.ShouldNotify(job.Kind, _activity))
        {
            return;
        }

        Dispatcher.UIThread.Post(() => _ = NotifyAsync(job));
    }

    private async Task NotifyAsync(JobSnapshot job)
    {
        string? name = null;
        if (job.GameId is { } gameId && await _games.ListAsync() is { Ok: true } listed)
        {
            name = listed.Value.FirstOrDefault(game => game.Id == gameId)?.Name;
        }

        var (title, body) = NotificationText.For(job, name, _text);
        _notifications.Show(title, body);
    }

    private void OnActivationRequested(object? sender, EventArgs e) => Dispatcher.UIThread.Post(BringForward);

    private void BringForward()
    {
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Show();
        // GNOME ignores the stale timestamp of Avalonia's own request, so under X11 the request is sent stamped "now" instead.
        if (OperatingSystem.IsLinux() && _window.TryGetPlatformHandle() is { HandleDescriptor: "XID" } handle && X11WindowRaiser.TryRaise(handle.Handle))
        {
            return;
        }

        _window.Activate();
    }
}
