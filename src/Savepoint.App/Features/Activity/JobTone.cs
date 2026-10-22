namespace Savepoint.App.Features.Activity;

/// <summary>How a job's state is drawn: the icon and the status take the matching color.</summary>
public enum JobTone
{
    /// <summary>The job is running, or stopping.</summary>
    Running,

    /// <summary>The job finished all its work.</summary>
    Success,

    /// <summary>The job finished only in part.</summary>
    Warning,

    /// <summary>The job failed.</summary>
    Error,

    /// <summary>The job was canceled.</summary>
    Neutral,
}
