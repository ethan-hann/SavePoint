namespace Savepoint.Core.Api;

/// <summary>How much of a job's details SavePoint keeps in memory.</summary>
public static class JobHistory
{
    /// <summary>
    /// The most lines <see cref="IJobsApi.GetLines"/> returns for one job. A snapshot of a game with 20,000 files writes about that many
    /// names; a larger game keeps its newest ones, and a job holds a few megabytes at most however many files it handles.
    /// </summary>
    public const int MaxLines = 20_000;
}
