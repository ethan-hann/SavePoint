using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Activity;

/// <summary>The words the Activity screen uses for a job: what it is, how it stands, how far it has got, and when it ran.</summary>
public sealed class ActivityText
{
    // Too early or too late in a job, a guess at the time left would swing wildly or say nothing worth knowing.
    private const double _earliestShare = 0.05;
    private static readonly TimeSpan _earliestGuess = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _shortestGuess = TimeSpan.FromSeconds(3);

    private readonly UiFormatter _text;
    private readonly Func<string, string?> _gameName;
    private readonly TimeProvider _clock;

    /// <param name="text">Formats the catalog messages.</param>
    /// <param name="gameName">Looks up a game's name by id; null for a game that is no longer in the library.</param>
    /// <param name="clock">What "now" means for the time a running job has left.</param>
    public ActivityText(UiFormatter text, Func<string, string?> gameName, TimeProvider clock)
    {
        _text = text;
        _gameName = gameName;
        _clock = clock;
        Lines = new JobLineFormatter(text);
    }

    /// <summary>Formats the lines of a job's details.</summary>
    public JobLineFormatter Lines { get; }

    /// <summary>What the job is, in a short phrase that names its game.</summary>
    /// <param name="job">The job.</param>
    public string Title(JobSnapshot job)
    {
        var game = ("game", (object?)GameName(job));
        return job.Kind switch
        {
            JobKind.Launch => _text.Message("activity.job.launch", game),
            JobKind.Capture => _text.Message("activity.job.capture", game),
            JobKind.Compact when job.GameId is null => _text.Message("activity.job.compactAll"),
            JobKind.Compact => _text.Message("activity.job.compact", game),
            JobKind.RelocateStorage => _text.Message("activity.job.relocate"),
            _ => _text.Message("activity.job.clear"),
        };
    }

    /// <summary>Where the job stands: in progress, stopping, done, partly done, failed, or canceled.</summary>
    /// <param name="job">The job.</param>
    public string Status(JobSnapshot job) => job.Status switch
    {
        JobStatus.Running when job.CancelRequested => _text.Message("activity.status.stopping"),
        JobStatus.Running => _text.Message("activity.status.running"),
        JobStatus.Succeeded => _text.Message("activity.status.done"),
        JobStatus.Partial => _text.Message("activity.status.partial"),
        JobStatus.Failed => _text.Message("activity.status.failed"),
        _ => _text.Message("activity.status.canceled"),
    };

    /// <summary>How the job's state is drawn.</summary>
    /// <param name="job">The job.</param>
    public JobTone Tone(JobSnapshot job) => job.Status switch
    {
        JobStatus.Running => JobTone.Running,
        JobStatus.Succeeded => JobTone.Success,
        JobStatus.Partial => JobTone.Warning,
        JobStatus.Failed => JobTone.Error,
        _ => JobTone.Neutral,
    };

    /// <summary>How far the job has got, in bytes or files; empty when it counts neither.</summary>
    /// <param name="job">The job.</param>
    public string Progress(JobSnapshot job)
    {
        var progress = job.Progress;
        if (_text.Describe(progress) is { } described)
        {
            return described;
        }

        return progress.FilesDone is > 0 ? _text.Counting() : string.Empty;
    }

    /// <summary>The share of the work done, from 0 to 1, or null when the job cannot say.</summary>
    /// <param name="job">The job.</param>
    public double? Fraction(JobSnapshot job)
    {
        return ProgressWording.Fraction(job.Progress);
    }

    /// <summary>A guess at the time left, worked out from how fast the job has gone; empty when it is too early or too late to say.</summary>
    /// <param name="job">The job.</param>
    public string TimeLeft(JobSnapshot job)
    {
        if (job.Status != JobStatus.Running || job.CancelRequested || Fraction(job) is not { } share || share < _earliestShare || share >= 1)
        {
            return string.Empty;
        }

        var elapsed = _clock.GetUtcNow() - job.StartedAt;
        if (elapsed < _earliestGuess)
        {
            return string.Empty;
        }

        var left = TimeSpan.FromSeconds(Math.Round(elapsed.TotalSeconds * (1 - share) / share));
        return left < _shortestGuess ? string.Empty : _text.Message("activity.progress.timeLeft", ("time", _text.Duration(left)));
    }

    /// <summary>When the job started.</summary>
    /// <param name="job">The job.</param>
    public string Started(JobSnapshot job) => _text.Message("activity.row.started", ("when", _text.ShortDateTime(job.StartedAt)));

    /// <summary>When the job finished; empty while it runs.</summary>
    /// <param name="job">The job.</param>
    public string Finished(JobSnapshot job) =>
        job.FinishedAt is { } finished ? _text.Message("activity.row.finished", ("when", _text.ShortDateTime(finished))) : string.Empty;

    /// <summary>
    /// True while a launch is switching the game's folders: it has written its switch step and has not yet started the game, so a stop
    /// leaves the folder with a mix of two snapshots. A launch whose early lines have fallen off the list is taken to be switching.
    /// </summary>
    /// <param name="job">The job.</param>
    public static bool IsSwitching(JobSnapshot job) =>
        job is { Kind: JobKind.Launch, Status: JobStatus.Running }
        && !HasStep(job, JobSteps.LaunchStarting)
        && (job.LinesDropped > 0 || HasStep(job, JobSteps.LaunchSwitchFolder));

    private static bool HasStep(JobSnapshot job, string messageId) =>
        job.RecentLines.Any(line => line is JobLine.Step step && step.MessageId == messageId);

    /// <summary>What to say about stopping the job: that it is stopping, or why it cannot be stopped; empty when there is nothing to say.</summary>
    /// <param name="job">The job.</param>
    public string StopNote(JobSnapshot job)
    {
        if (job.Status != JobStatus.Running)
        {
            return string.Empty;
        }

        if (job.CancelRequested)
        {
            // A stopped switch is the one stop that leaves something behind, and the launch's own warning already says what.
            return IsSwitching(job) ? _text.Message("launch.progress.stopNote") : _text.Message("activity.stop.stopping");
        }

        if (job.Cancellable)
        {
            return string.Empty;
        }

        return job.Kind switch
        {
            JobKind.ClearData => _text.Message("activity.stop.clear"),
            JobKind.RelocateStorage => _text.Message("activity.stop.relocate"),
            JobKind.Compact => _text.Message("activity.stop.compact"),
            _ => _text.Message("activity.stop.generic"),
        };
    }

    /// <summary>The first thing that went wrong, or null; a partly done job names the first folder that failed.</summary>
    /// <param name="job">The job.</param>
    public ErrorInfo? Reason(JobSnapshot job) =>
        job.Status == JobStatus.Running ? null : job.Error ?? JobErrors.FolderErrors(job.Result).FirstOrDefault();

    /// <summary>"Profile: Vanilla".</summary>
    /// <param name="profile">The profile the job works on.</param>
    public string ProfileLine(JobProfile profile) => _text.Message("activity.row.profile", ("profile", profile.Name));

    /// <summary>The job in one sentence for a screen reader.</summary>
    /// <param name="job">The job.</param>
    public string Describe(JobSnapshot job)
    {
        var progress = Progress(job);
        return _text.Message(
            "activity.row.automationName",
            ("title", Title(job)),
            ("status", Status(job).TrimEnd('…')),
            ("hasProfile", job.Profile is null ? "no" : "yes"),
            ("profile", job.Profile is null ? string.Empty : ProfileLine(job.Profile)),
            ("hasProgress", progress.Length == 0 ? "no" : "yes"),
            ("progress", progress),
            ("started", Started(job)));
    }

    private string GameName(JobSnapshot job) =>
        (job.GameId is null ? null : _gameName(job.GameId)) ?? _text.Message("activity.job.unknownGame");
}
