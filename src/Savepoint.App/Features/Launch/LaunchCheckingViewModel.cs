using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Formatting;

namespace Savepoint.App.Features.Launch;

/// <summary>
/// The inline card shown at once when Play is pressed, while SavePoint checks both folders for what starting would change. The
/// scan can take seconds on a large modded install, and without this card the only sign of work is Play turning grey.
/// </summary>
public sealed partial class LaunchCheckingViewModel : ObservableObject
{
    private readonly Action _cancel;

    /// <summary>True once Cancel has been pressed and the check has not ended yet.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isCancelling;

    /// <param name="text">Formats the card's lines.</param>
    /// <param name="cancel">Stops the check.</param>
    public LaunchCheckingViewModel(UiFormatter text, Action cancel)
    {
        _cancel = cancel;
        Title = text.Message("launch.checking.title");
        Hint = text.Message("launch.checking.hint");
    }

    /// <summary>"Checking your files…".</summary>
    public string Title { get; }

    /// <summary>A line under the title that says why this can take a moment.</summary>
    public string Hint { get; }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        IsCancelling = true;
        _cancel();
    }

    private bool CanCancel() => !IsCancelling;
}
