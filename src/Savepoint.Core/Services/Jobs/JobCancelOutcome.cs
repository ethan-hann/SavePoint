namespace Savepoint.Core.Services.Jobs;

/// <summary>What <see cref="JobRegistry.Cancel"/> did with a cancel request.</summary>
public enum JobCancelOutcome
{
    /// <summary>The job was running and cancellable; its token is now cancelled and its snapshot says so.</summary>
    Requested,

    /// <summary>An earlier request already cancelled the job; nothing changed.</summary>
    AlreadyRequested,

    /// <summary>No job with that id is known, including one old enough to have been pruned.</summary>
    Unknown,

    /// <summary>The job has already ended; nothing changed.</summary>
    Finished,

    /// <summary>The job is running but was started as not cancellable; nothing changed.</summary>
    NotCancellable,
}
