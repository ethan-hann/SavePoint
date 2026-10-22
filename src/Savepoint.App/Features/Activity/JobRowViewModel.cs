using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.App.Theme;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Activity;

/// <summary>One job in the Activity list: what it is, which profile, how it stands, how far it has got, and when it ran.</summary>
public sealed class JobRowViewModel : ObservableObject
{
    private readonly ActivityText _words;
    private State _state;
    private JobSnapshot _job;

    /// <param name="job">The job's latest snapshot.</param>
    /// <param name="words">Words for the job.</param>
    public JobRowViewModel(JobSnapshot job, ActivityText words)
    {
        _words = words;
        _job = job;
        _state = StateOf(job);
        JobId = job.JobId;
    }

    /// <summary>The job's id.</summary>
    public Guid JobId { get; }

    /// <summary>The latest snapshot of the job; read it when needed, it does not raise a change.</summary>
    public JobSnapshot Job => _job;

    /// <summary>What the job is.</summary>
    public string Title => _state.Title;

    /// <summary>"Profile: Vanilla", or empty when the job works on no one profile.</summary>
    public string ProfileText => _state.ProfileText;

    /// <summary>True when the job names a profile.</summary>
    public bool HasProfile => ProfileText.Length > 0;

    /// <summary>Where the job stands.</summary>
    public string StatusText => _state.StatusText;

    /// <summary>How the job's state is drawn.</summary>
    public JobTone Tone => _state.Tone;

    /// <summary>True while the job runs (the state is drawn in the accent color).</summary>
    public bool IsToneRunning => Tone == JobTone.Running;

    /// <summary>True when the job finished all its work.</summary>
    public bool IsToneSuccess => Tone == JobTone.Success;

    /// <summary>True when the job finished only in part.</summary>
    public bool IsToneWarning => Tone == JobTone.Warning;

    /// <summary>True when the job failed.</summary>
    public bool IsToneError => Tone == JobTone.Error;

    /// <summary>True when the job was canceled.</summary>
    public bool IsToneNeutral => Tone == JobTone.Neutral;

    /// <summary>The Material Symbols glyph for the job's state.</summary>
    public string Icon => Tone switch
    {
        JobTone.Running => Icons.Sync,
        JobTone.Success => Icons.CheckCircle,
        JobTone.Warning => Icons.Warning,
        JobTone.Error => Icons.Error,
        _ => Icons.Cancel,
    };

    /// <summary>True while the job runs, including while it stops.</summary>
    public bool IsRunning => _state.IsRunning;

    /// <summary>True while a bar shows: the job runs.</summary>
    public bool ShowProgress => IsRunning;

    /// <summary>True when the bar cannot say how far the job has got.</summary>
    public bool IsIndeterminate => _state.Percent is null;

    /// <summary>How far the job has got, from 0 to 100.</summary>
    public double ProgressPercent => _state.Percent ?? 0;

    /// <summary>"12 of 40 files" or "3.1 MB of 41.2 MB"; empty when the job counts neither.</summary>
    public string ProgressText => _state.ProgressText;

    /// <summary>True when <see cref="ProgressText"/> has something to say.</summary>
    public bool HasProgressText => ProgressText.Length > 0;

    /// <summary>"About 2 minutes left"; empty when it is too early or too late to say.</summary>
    public string TimeLeftText => _state.TimeLeftText;

    /// <summary>True when <see cref="TimeLeftText"/> has something to say.</summary>
    public bool HasTimeLeft => TimeLeftText.Length > 0;

    /// <summary>"Started Jul 5, 11:42 PM".</summary>
    public string StartedText => _state.StartedText;

    /// <summary>"Finished Jul 5, 11:43 PM"; empty while the job runs.</summary>
    public string FinishedText => _state.FinishedText;

    /// <summary>True when the job has finished.</summary>
    public bool HasFinished => FinishedText.Length > 0;

    /// <summary>That the job is stopping, or why it cannot be stopped; empty when there is nothing to say.</summary>
    public string StopNote => _state.StopNote;

    /// <summary>True when <see cref="StopNote"/> has something to say.</summary>
    public bool HasStopNote => StopNote.Length > 0;

    /// <summary>The job in one sentence for a screen reader.</summary>
    public string AutomationName => _state.AutomationName;

    /// <summary>Takes a newer snapshot of the job and says which of the row's properties changed.</summary>
    /// <param name="job">The snapshot.</param>
    public void Apply(JobSnapshot job)
    {
        var before = _state;
        _job = job;
        _state = StateOf(job);
        Changed(before.Title, _state.Title, nameof(Title));
        Changed(before.ProfileText, _state.ProfileText, nameof(ProfileText), nameof(HasProfile));
        Changed(before.StatusText, _state.StatusText, nameof(StatusText));
        Changed(before.Tone, _state.Tone, nameof(Tone), nameof(Icon), nameof(IsToneRunning), nameof(IsToneSuccess), nameof(IsToneWarning), nameof(IsToneError), nameof(IsToneNeutral));
        Changed(before.IsRunning, _state.IsRunning, nameof(IsRunning), nameof(ShowProgress));
        Changed(before.Percent, _state.Percent, nameof(IsIndeterminate), nameof(ProgressPercent));
        Changed(before.ProgressText, _state.ProgressText, nameof(ProgressText), nameof(HasProgressText));
        Changed(before.TimeLeftText, _state.TimeLeftText, nameof(TimeLeftText), nameof(HasTimeLeft));
        Changed(before.StartedText, _state.StartedText, nameof(StartedText));
        Changed(before.FinishedText, _state.FinishedText, nameof(FinishedText), nameof(HasFinished));
        Changed(before.StopNote, _state.StopNote, nameof(StopNote), nameof(HasStopNote));
        Changed(before.AutomationName, _state.AutomationName, nameof(AutomationName));
    }

    private void Changed<T>(T before, T after, params string[] names)
    {
        if (EqualityComparer<T>.Default.Equals(before, after))
        {
            return;
        }

        foreach (var name in names)
        {
            OnPropertyChanged(name);
        }
    }

    private State StateOf(JobSnapshot job) =>
        new(
            _words.Title(job),
            job.Profile is { } profile ? _words.ProfileLine(profile) : string.Empty,
            _words.Status(job),
            _words.Tone(job),
            job.Status == JobStatus.Running,
            _words.Fraction(job) is { } share ? share * 100 : null,
            _words.Progress(job),
            _words.TimeLeft(job),
            _words.Started(job),
            _words.Finished(job),
            _words.StopNote(job),
            _words.Describe(job));

    private sealed record State(
        string Title,
        string ProfileText,
        string StatusText,
        JobTone Tone,
        bool IsRunning,
        double? Percent,
        string ProgressText,
        string TimeLeftText,
        string StartedText,
        string FinishedText,
        string StopNote,
        string AutomationName);
}
