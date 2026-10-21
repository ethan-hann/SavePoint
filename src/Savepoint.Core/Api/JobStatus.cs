namespace Savepoint.Core.Api;

public enum JobStatus
{
    Running,
    Succeeded,

    // At least one folder succeeded and at least one failed; never reported as success (D13.20).
    Partial,
    Failed,
    Cancelled,
}
