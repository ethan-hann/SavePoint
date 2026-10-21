using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// Collects progress reports synchronously, on whichever thread reports them.
internal sealed class ProgressLog : IProgress<FileProgress>
{
    private readonly object _gate = new();
    private readonly List<FileProgress> _reports = [];

    // Runs on the reporting thread after each report is stored.
    public Action<FileProgress>? OnReport { get; set; }

    public IReadOnlyList<FileProgress> Reports
    {
        get
        {
            lock (_gate)
            {
                return [.. _reports];
            }
        }
    }

    // The newest report; the test fails by assertion when there is none.
    public FileProgress Last
    {
        get
        {
            var reports = Reports;
            Assert.NotEmpty(reports);
            return reports[^1];
        }
    }

    public void Report(FileProgress value)
    {
        lock (_gate)
        {
            _reports.Add(value);
        }

        OnReport?.Invoke(value);
    }
}
