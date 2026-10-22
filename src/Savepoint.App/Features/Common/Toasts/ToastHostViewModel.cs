using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Theme;

namespace Savepoint.App.Features.Common.Toasts;

/// <summary>How a toast is drawn and how long it stays.</summary>
public enum ToastKind
{
    /// <summary>Something finished; the success color.</summary>
    Success,

    /// <summary>Neutral news, such as something the user canceled.</summary>
    Info,

    /// <summary>Something finished only in part; stays until dismissed.</summary>
    Warning,

    /// <summary>Something failed; stays until dismissed.</summary>
    Error,
}

/// <summary>A transient message.</summary>
/// <param name="Text">The message, already localized.</param>
/// <param name="Kind">Drawn in the matching color.</param>
/// <param name="Details">A second line, such as why something failed.</param>
/// <param name="Duration">How long it stays; 6 seconds for success and info when null, and until dismissed for warnings and errors.</param>
public sealed record ToastMessage(string Text, ToastKind Kind = ToastKind.Info, string? Details = null, TimeSpan? Duration = null);

/// <summary>One toast on screen.</summary>
public sealed partial class ToastViewModel : ObservableObject
{
    private readonly Action<ToastViewModel> _dismiss;

    internal ToastViewModel(ToastMessage message, Action<ToastViewModel> dismiss)
    {
        Message = message;
        _dismiss = dismiss;
        Icon = message.Kind switch
        {
            ToastKind.Success => Icons.CheckCircle,
            ToastKind.Warning => Icons.Warning,
            ToastKind.Error => Icons.Error,
            _ => Icons.Info,
        };
    }

    /// <summary>What was shown.</summary>
    public ToastMessage Message { get; }

    /// <summary>The message text.</summary>
    public string Text => Message.Text;

    /// <summary>True when the toast stays until dismissed: a warning or error that named no duration.</summary>
    public bool IsPersistent => Message.Duration is null && Message.Kind is ToastKind.Warning or ToastKind.Error;

    /// <summary>The second line, or null.</summary>
    public string? Details => Message.Details;

    /// <summary>True when there is a second line.</summary>
    public bool HasDetails => !string.IsNullOrEmpty(Message.Details);

    /// <summary>The Material Symbols glyph for the kind.</summary>
    public string Icon { get; }

    /// <summary>True for a success toast; the icon is drawn in the success color.</summary>
    public bool IsSuccess => Message.Kind == ToastKind.Success;

    /// <summary>True for a warning toast.</summary>
    public bool IsWarning => Message.Kind == ToastKind.Warning;

    /// <summary>True for an error toast.</summary>
    public bool IsError => Message.Kind == ToastKind.Error;

    /// <summary>Removes the toast now.</summary>
    [RelayCommand]
    private void Dismiss() => _dismiss(this);
}

/// <summary>
/// The messages below the page. Success and info toasts leave on their own after a few seconds; warnings and errors stay
/// until dismissed and are never pushed out by newer ones, so a failure is always there to read. At most
/// <see cref="MaxVisible"/> timed toasts show at once, so the oldest of those goes when a new one arrives. Holding the
/// pointer or keyboard focus on the toasts pauses every timer (<see cref="SetPaused"/>). Each new toast is also
/// published in <see cref="Announcement"/>, which the view exposes as a polite live region.
/// </summary>
public sealed partial class ToastHostViewModel : ObservableObject
{
    /// <summary>The most timed toasts shown at once; warnings and errors are not counted.</summary>
    public const int MaxVisible = 3;

    private static readonly TimeSpan _stay = TimeSpan.FromSeconds(6);

    // A toast let go after a pause still gets time to be read.
    private static readonly TimeSpan _minimumAfterPause = TimeSpan.FromSeconds(3);

    private readonly TimeProvider _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly Dictionary<ToastViewModel, Countdown> _countdowns = [];

    [ObservableProperty]
    private string _announcement = string.Empty;

    /// <param name="clock">Times the auto-dismissal.</param>
    /// <param name="dispatcher">Brings the timer's callback back to the UI thread.</param>
    public ToastHostViewModel(TimeProvider clock, IUiDispatcher dispatcher)
    {
        _clock = clock;
        _dispatcher = dispatcher;
        Toasts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMany));
            OnPropertyChanged(nameof(HasToasts));
        };
    }

    /// <summary>The toasts on screen, oldest first.</summary>
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    /// <summary>True while any toast shows.</summary>
    public bool HasToasts => Toasts.Count > 0;

    /// <summary>True when more than one toast shows, which is when Dismiss all is offered.</summary>
    public bool HasMany => Toasts.Count > 1;

    /// <summary>True while the pointer or keyboard focus is on the toasts, which holds every timer.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>
    /// Shows a toast and starts its timer, unless it stays until dismissed or the toasts are paused. A warning or error that
    /// is already showing word for word is not listed again; it is announced again and the same toast comes back.
    /// </summary>
    public ToastViewModel Show(ToastMessage message)
    {
        // Clearing first makes an identical message count as a change, so a repeat is announced again.
        Announcement = string.Empty;
        Announcement = message.Details is { Length: > 0 } details ? $"{message.Text} {details}" : message.Text;

        if (Toasts.FirstOrDefault(shown => shown.IsPersistent && shown.Message == message) is { } already)
        {
            return already;
        }

        var toast = new ToastViewModel(message, Dismiss);
        Toasts.Add(toast);

        if (!toast.IsPersistent)
        {
            // Only timed toasts are limited; a problem nobody has dismissed is never pushed out.
            while (Toasts.Count(shown => !shown.IsPersistent) > MaxVisible)
            {
                Dismiss(Toasts.First(shown => !shown.IsPersistent));
            }

            var countdown = new Countdown(message.Duration ?? _stay);
            _countdowns[toast] = countdown;
            if (!IsPaused)
            {
                Arm(toast, countdown);
            }
        }

        return toast;
    }

    /// <summary>Holds or releases every timer; releasing gives each toast the time it had left, and at least three seconds.</summary>
    /// <param name="paused">True while the pointer or keyboard focus is on the toasts.</param>
    public void SetPaused(bool paused)
    {
        if (paused == IsPaused)
        {
            return;
        }

        IsPaused = paused;
        foreach (var (toast, countdown) in _countdowns.ToList())
        {
            if (paused)
            {
                countdown.Hold(_clock.GetUtcNow());
            }
            else
            {
                countdown.Remaining = countdown.Remaining < _minimumAfterPause ? _minimumAfterPause : countdown.Remaining;
                Arm(toast, countdown);
            }
        }
    }

    /// <summary>Removes every toast.</summary>
    [RelayCommand]
    public void DismissAll()
    {
        foreach (var toast in Toasts.ToList())
        {
            Dismiss(toast);
        }
    }

    /// <summary>Removes a toast; one that is already gone is ignored.</summary>
    public void Dismiss(ToastViewModel toast)
    {
        if (_countdowns.Remove(toast, out var countdown))
        {
            countdown.Timer?.Dispose();
        }

        Toasts.Remove(toast);
    }

    private void Arm(ToastViewModel toast, Countdown countdown)
    {
        countdown.Timer?.Dispose();
        countdown.ArmedAt = _clock.GetUtcNow();
        countdown.Timer = _clock.CreateTimer(_ => _dispatcher.Post(() => Dismiss(toast)), null, countdown.Remaining, Timeout.InfiniteTimeSpan);
    }

    // How long a timed toast has left, and the timer that is running it down while it does.
    private sealed class Countdown(TimeSpan remaining)
    {
        public TimeSpan Remaining { get; set; } = remaining;

        public DateTimeOffset ArmedAt { get; set; }

        public ITimer? Timer { get; set; }

        public void Hold(DateTimeOffset now)
        {
            if (Timer is null)
            {
                return;
            }

            Timer.Dispose();
            Timer = null;
            Remaining = TimeSpan.FromTicks(Math.Max(0, (Remaining - (now - ArmedAt)).Ticks));
        }
    }
}
