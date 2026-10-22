using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Launch;

/// <summary>
/// The progress dialog shown while a profile is switched in (mockup 2c): what is being put back, a bar that is
/// determinate once the job reports totals, the bytes or files done with a time left, the path being written, and
/// Cancel. It cannot be dismissed; the launch flow closes it when the switch ends. Progress is announced at each quarter through
/// <see cref="Announcement"/>.
/// </summary>
public sealed partial class SwitchProgressDialogViewModel : DialogViewModel<bool>
{
    private readonly UiFormatter _text;
    private readonly TimeProvider _clock;
    private readonly Func<Task<bool>> _cancel;
    private DateTimeOffset? _firstReport;
    private readonly ProgressMilestones _milestones = new();

    /// <summary>True while the bar has no total to fill against.</summary>
    [ObservableProperty]
    private bool _isIndeterminate = true;

    /// <summary>How full the bar is, from 0 to 100.</summary>
    [ObservableProperty]
    private double _percentValue;

    /// <summary>The percentage as text ("62%"); empty while the bar is indeterminate.</summary>
    [ObservableProperty]
    private string _percentText = string.Empty;

    /// <summary>The line under the bar: bytes or files done and the time left, or a hint before anything is known.</summary>
    [ObservableProperty]
    private string _progressText;

    /// <summary>The path being written now; null before the first report.</summary>
    [ObservableProperty]
    private string? _currentPath;

    /// <summary>True once Cancel has been asked for and the job has not ended yet.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isCancelling;

    /// <summary>What the hidden live region says: the start, each quarter, and the cancel request.</summary>
    [ObservableProperty]
    private string _announcement;

    /// <param name="gameName">The game being prepared.</param>
    /// <param name="target">The install state being put back, for the one-line explanation; null when it is not known.</param>
    /// <param name="text">Formats every message.</param>
    /// <param name="clock">Times the switch, for the time left.</param>
    /// <param name="cancel">Asks the service to cancel the launch job; answers false when it could not.</param>
    public SwitchProgressDialogViewModel(string gameName, InstallState? target, UiFormatter text, TimeProvider clock, Func<Task<bool>> cancel)
        : base(text.Message("launch.progress.title", ("gameName", gameName)))
    {
        _text = text;
        _clock = clock;
        _cancel = cancel;
        Explanation = ExplanationFor(target);
        _progressText = text.Message("launch.progress.checking");
        _announcement = text.Message("launch.progress.announceStart", ("gameName", gameName));
    }

    /// <summary>The line under the title: what is being put back in place.</summary>
    public string Explanation { get; }

    /// <inheritdoc />
    public override bool CanDismiss => false;

    /// <inheritdoc />
    public override string Description => Explanation;

    /// <inheritdoc />
    public override double DialogWidth => 600;

    /// <inheritdoc />
    protected override bool DismissResult => false;

    /// <summary>Takes the newest progress report from the job.</summary>
    public void Update(JobProgress progress)
    {
        var now = _clock.GetUtcNow();
        _firstReport ??= now;
        CurrentPath = progress.CurrentPath;

        // Git removes files before it writes any, and a removal has no bytes, so the bar and the line follow whichever of the files and
        // the bytes is further along: a switch that mostly removes files moves from the start.
        var fileShare = progress.FilesTotal is > 0 && progress.FilesDone is { } done ? (double)done / progress.FilesTotal.Value : (double?)null;
        var byteShare = progress.BytesTotal is > 0 && progress.BytesDone is { } written ? (double)written / progress.BytesTotal.Value : (double?)null;
        if (byteShare is { } bytes && !(fileShare > bytes))
        {
            var bytesDone = progress.BytesDone!.Value;
            var total = progress.BytesTotal!.Value;
            var remaining = LaunchTiming.Remaining(bytesDone, total, now - _firstReport.Value);
            ProgressText = remaining is { } left
                ? _text.Message(
                    "launch.progress.bytesWithTime",
                    ("done", _text.FileSize(bytesDone)),
                    ("total", _text.FileSize(total)),
                    ("time", _text.Duration(LaunchTiming.RoundForDisplay(left))))
                : _text.Message("launch.progress.bytes", ("done", _text.FileSize(bytesDone)), ("total", _text.FileSize(total)));
            ShowFraction((double)bytesDone / total);
        }
        else if (fileShare is { } files)
        {
            var filesDone = progress.FilesDone!.Value;
            var filesTotal = progress.FilesTotal!.Value;
            ProgressText = LaunchTiming.RemainingAtShare(files, now - _firstReport.Value) is { } left
                ? _text.Message(
                    "launch.progress.filesWithTime",
                    ("done", _text.Number(filesDone)),
                    ("total", _text.Number(filesTotal)),
                    ("time", _text.Duration(LaunchTiming.RoundForDisplay(left))))
                : _text.Message("launch.progress.files", ("done", _text.Number(filesDone)), ("total", _text.Number(filesTotal)));
            ShowFraction(files);
        }
        else
        {
            IsIndeterminate = true;
            PercentText = string.Empty;
            ProgressText = progress.FilesDone is { } counted
                ? _text.Message("launch.progress.filesSoFar", ("done", _text.Number(counted)))
                : _text.Message("launch.progress.hint");
        }
    }

    /// <summary>Closes the dialog; the launch flow calls it when the switch has ended either way.</summary>
    public void Finish() => Close(true);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync()
    {
        IsCancelling = true;
        Announcement = string.Empty;
        Announcement = _text.Message("launch.progress.announceCancelling");
        if (!await _cancel())
        {
            IsCancelling = false;
        }
    }

    private bool CanCancel() => !IsCancelling;

    private void ShowFraction(double fraction)
    {
        var clamped = Math.Clamp(fraction, 0, 1);
        IsIndeterminate = false;
        PercentValue = clamped * 100;
        var percent = (int)Math.Floor(PercentValue);
        PercentText = _text.Message("launch.progress.percent", ("percent", percent));

        if (_milestones.Crossed(percent) is { } crossed)
        {
            Announcement = _text.Message("launch.progress.announcePercent", ("percent", crossed));
        }
    }

    private string ExplanationFor(InstallState? target)
    {
        if (target?.Version is not { Length: > 0 } version)
        {
            return _text.Message("launch.progress.hint");
        }

        var state = target.ModFileCount is > 0 and var mods
            ? _text.Message("launch.progress.state.versionAndMods", ("version", version), ("count", mods))
            : _text.Message("launch.progress.state.version", ("version", version));
        return _text.Message("launch.progress.putting", ("state", state));
    }
}
