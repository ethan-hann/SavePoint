using Savepoint.Core.Git;
using Savepoint.Core.Tests.Git.Engine;

namespace Savepoint.Core.Tests.Git;

// The sampler turns the object store's growth since staging began into byte reports; the clock and the measuring are injected.
public sealed class StagingByteSamplerTests
{
    private static ProgressReporter ReporterOver(ProgressLog log, long total)
    {
        var reporter = new ProgressReporter(log);
        reporter.SetByteTotal(total);
        return reporter;
    }

    // Lets the sampler run through the given number of waits, then stops it the way the engine does.
    private static Func<CancellationToken, Task> WaitsThenStops(int waits, CancellationTokenSource stop)
    {
        var count = 0;
        return _ =>
        {
            if (++count > waits)
            {
                stop.Cancel();
                throw new OperationCanceledException(stop.Token);
            }

            return Task.CompletedTask;
        };
    }

    private static Func<long> Measures(params long[] sizes)
    {
        var index = 0;
        return () => sizes[Math.Min(index++, sizes.Length - 1)];
    }

    [Fact]
    public async Task Each_sample_reports_the_growth_since_the_baseline()
    {
        var log = new ProgressLog();
        using var stop = new CancellationTokenSource();

        await StagingByteSampler.RunAsync(Measures(100, 160, 300, 300), ReporterOver(log, 1000), WaitsThenStops(2, stop), stop.Token);

        Assert.Equal([60L, 200L], log.Reports.Select(report => report.BytesDone!.Value).Distinct());
    }

    [Fact]
    public async Task A_last_sample_is_taken_when_the_sampler_is_stopped()
    {
        var log = new ProgressLog();
        using var stop = new CancellationTokenSource();

        await StagingByteSampler.RunAsync(Measures(0, 50), ReporterOver(log, 1000), WaitsThenStops(0, stop), stop.Token);

        Assert.Equal(50, log.Last.BytesDone);
    }

    [Fact]
    public async Task A_measurement_that_fails_does_not_stop_the_sampling()
    {
        var log = new ProgressLog();
        using var stop = new CancellationTokenSource();
        var calls = 0;
        long Measure() => ++calls switch
        {
            1 => 0,
            2 => throw new IOException("a file vanished"),
            _ => 90,
        };

        var error = await Record.ExceptionAsync(() => StagingByteSampler.RunAsync(Measure, ReporterOver(log, 1000), WaitsThenStops(2, stop), stop.Token));

        Assert.Null(error);
        Assert.Equal(90, log.Last.BytesDone);
    }

    [Fact]
    public async Task A_store_that_shrinks_below_the_baseline_reports_nothing_negative()
    {
        var log = new ProgressLog();
        using var stop = new CancellationTokenSource();

        await StagingByteSampler.RunAsync(Measures(500, 100, 600, 600), ReporterOver(log, 1000), WaitsThenStops(2, stop), stop.Token);

        Assert.Equal([100L], log.Reports.Select(report => report.BytesDone!.Value).Distinct());
    }
}
