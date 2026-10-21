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

    /// <summary>Something finished only in part; stays longer.</summary>
    Warning,

    /// <summary>Something failed; stays longer.</summary>
    Error,
}

/// <summary>A transient message.</summary>
/// <param name="Text">The message, already localized.</param>
/// <param name="Kind">Drawn in the matching color.</param>
/// <param name="Details">A second line, such as why something failed.</param>
/// <param name="Duration">How long it stays; 6 seconds for success and info, 10 for warnings and errors when null.</param>
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
/// The transient messages at the bottom of the window. A toast leaves on its own after a few seconds or when
/// dismissed; at most <see cref="MaxVisible"/> show at once, so the oldest goes when a new one arrives. Each new
/// toast is also published in <see cref="Announcement"/>, which the view exposes as a polite live region.
/// </summary>
public sealed partial class ToastHostViewModel : ObservableObject
{
    /// <summary>The most toasts shown at once.</summary>
    public const int MaxVisible = 3;

    private static readonly TimeSpan _shortStay = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan _longStay = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly Dictionary<ToastViewModel, ITimer> _timers = [];

    [ObservableProperty]
    private string _announcement = string.Empty;

    /// <param name="clock">Times the auto-dismissal.</param>
    /// <param name="dispatcher">Brings the timer's callback back to the UI thread.</param>
    public ToastHostViewModel(TimeProvider clock, IUiDispatcher dispatcher)
    {
        _clock = clock;
        _dispatcher = dispatcher;
    }

    /// <summary>The toasts on screen, oldest first.</summary>
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    /// <summary>Shows a toast and starts its timer.</summary>
    public ToastViewModel Show(ToastMessage message)
    {
        var toast = new ToastViewModel(message, Dismiss);
        Toasts.Add(toast);

        // Clearing first makes an identical message count as a change, so a repeat is announced again.
        Announcement = string.Empty;
        Announcement = message.Details is { Length: > 0 } details ? $"{message.Text} {details}" : message.Text;

        while (Toasts.Count > MaxVisible)
        {
            Dismiss(Toasts[0]);
        }

        var stay = message.Duration ?? (message.Kind is ToastKind.Warning or ToastKind.Error ? _longStay : _shortStay);
        _timers[toast] = _clock.CreateTimer(_ => _dispatcher.Post(() => Dismiss(toast)), null, stay, Timeout.InfiniteTimeSpan);
        return toast;
    }

    /// <summary>Removes a toast; one that is already gone is ignored.</summary>
    public void Dismiss(ToastViewModel toast)
    {
        if (_timers.Remove(toast, out var timer))
        {
            timer.Dispose();
        }

        Toasts.Remove(toast);
    }
}
