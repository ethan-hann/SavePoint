using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Launch;

/// <summary>
/// The inline card shown while the capture that follows a play session runs: a title, a bar that is determinate once
/// the job reports a file total, and Cancel. The shell's job pill also shows the capture; this card stays with the
/// game it belongs to.
/// </summary>
public sealed partial class LaunchSavingViewModel : ObservableObject
{
    private readonly UiFormatter _text;
    private readonly Func<Task<bool>> _cancel;

    /// <summary>True while the bar has no file total to fill against.</summary>
    [ObservableProperty]
    private bool _isIndeterminate = true;

    /// <summary>How full the bar is, from 0 to 100.</summary>
    [ObservableProperty]
    private double _percentValue;

    /// <summary>The line under the bar: files done, or a hint before anything is known.</summary>
    [ObservableProperty]
    private string _progressText;

    /// <summary>True once Cancel has been asked for and the job has not ended yet.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isCancelling;

    /// <param name="text">Formats the card's lines.</param>
    /// <param name="cancel">Asks the service to cancel the capture job; answers false when it could not.</param>
    public LaunchSavingViewModel(UiFormatter text, Func<Task<bool>> cancel)
    {
        _text = text;
        _cancel = cancel;
        Title = text.Message("launch.saving.title");
        _progressText = text.Message("launch.saving.hint");
    }

    /// <summary>"Saving your session…".</summary>
    public string Title { get; }

    /// <summary>Takes the newest progress report from the capture job.</summary>
    public void Update(JobProgress progress)
    {
        if (progress.FilesTotal is > 0 && progress.FilesDone is { } done)
        {
            IsIndeterminate = false;
            PercentValue = Math.Clamp((double)done / progress.FilesTotal.Value, 0, 1) * 100;
            ProgressText = _text.Message("launch.progress.files", ("done", _text.Number(done)), ("total", _text.Number(progress.FilesTotal.Value)));
        }
        else if (progress.FilesDone is { } count)
        {
            IsIndeterminate = true;
            ProgressText = _text.Message("launch.progress.filesSoFar", ("done", _text.Number(count)));
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync()
    {
        IsCancelling = true;
        if (!await _cancel())
        {
            IsCancelling = false;
        }
    }

    private bool CanCancel() => !IsCancelling;
}
