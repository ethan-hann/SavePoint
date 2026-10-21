namespace Savepoint.Core.Api;

/// <summary>Where a job is in its life. Anything but <see cref="Running"/> is final.</summary>
public enum JobStatus
{
    /// <summary>The job is still working, including after a cancel was requested and before it has stopped.</summary>
    Running,

    /// <summary>All the work finished: every folder was saved, or the launch reached a running game.</summary>
    Succeeded,

    /// <summary>At least one folder succeeded and at least one failed. Never reported as success (D13.20).</summary>
    Partial,

    /// <summary>The work could not be done, or every folder failed.</summary>
    Failed,

    /// <summary>The job stopped because the user cancelled it, or because optimization yielded to other work (D13.17).</summary>
    Cancelled,
}
