namespace Savepoint.Core.Git;

public sealed record GitRunnerOptions
{
    public int ReadBufferSize { get; init; } = 16 * 1024;

    public TimeSpan KillWaitTimeout { get; init; } = TimeSpan.FromSeconds(5);

    // A leftover grandchild holding a pipe open cannot keep a call waiting longer than this.
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
