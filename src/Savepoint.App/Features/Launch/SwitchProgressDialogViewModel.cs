using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Launch;

/// <summary>
/// The progress dialog shown while a profile is switched in (mockup 2c): what is being put back, a bar that is
/// determinate once the job reports totals, the bytes or files done with a time left, the path being written, and
/// Cancel. It cannot be dismissed; the launch flow closes it when the switch ends. Milestones are announced through
/// <see cref="Announcement"/>.
/// </summary>
public sealed partial class SwitchProgressDialogViewModel : DialogViewModel<bool>
{
    private static readonly int[] _milestones = [25, 50, 75];

    private readonly UiFormatter _text;
    private readonly TimeProvider _clock;
    private readonly Func<Task<bool>> _cancel;
    private DateTimeOffset? _firstReport;
    private int _announcedMilestone;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    [ObservableProperty]
    private double _percentValue;

    [ObservableProperty]
    private string _percentText = string.Empty;

    [ObservableProperty]
    private string _progressText;

    [ObservableProperty]
    private string? _currentPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isCancelling;

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
    public override double DialogWidth => 600;

    /// <inheritdoc />
    protected override bool DismissResult => false;

    /// <summary>Takes the newest progress report from the job.</summary>
    public void Update(JobProgress progress)
    {
        var now = _clock.GetUtcNow();
        _firstReport ??= now;
        CurrentPath = progress.CurrentPath;

        if (progress.BytesTotal is > 0 && progress.BytesDone is { } bytesDone)
        {
            var total = progress.BytesTotal.Value;
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
        else if (progress.FilesTotal is > 0 && progress.FilesDone is { } filesDone)
        {
            ProgressText = _text.Message("launch.progress.files", ("done", _text.Number(filesDone)), ("total", _text.Number(progress.FilesTotal.Value)));
            ShowFraction((double)filesDone / progress.FilesTotal.Value);
        }
        else
        {
            IsIndeterminate = true;
            PercentText = string.Empty;
            ProgressText = progress.FilesDone is { } done
                ? _text.Message("launch.progress.filesSoFar", ("done", _text.Number(done)))
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

        // Only the highest milestone crossed since the last report is spoken, so a jump does not queue several lines.
        var crossed = _milestones.Where(milestone => milestone <= percent && milestone > _announcedMilestone).DefaultIfEmpty(0).Max();
        if (crossed > 0)
        {
            _announcedMilestone = crossed;
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
