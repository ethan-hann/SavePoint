using Savepoint.Core.Git;
using Savepoint.Core.Tests.Git.Engine;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.Git;

// The sampler turns the object store's growth since staging began into byte reports; the clock and the measuring are injected.
public sealed class StagingByteSamplerTests
{
    private static ProgressReporter ReporterOver(ProgressLog log, long total, TimeProvider? clock = null)
    {
        var reporter = new ProgressReporter(log, time: clock);
        reporter.SetByteTotal(total);
        return reporter;
    }

    // Lets the sampler run through the given number of waits, then stops it the way the engine does. Each wait moves the clock on.
    private static Func<CancellationToken, Task> WaitsThenStops(int waits, CancellationTokenSource stop, ManualTimeProvider? clock = null, TimeSpan step = default)
    {
        var count = 0;
        return _ =>
        {
            if (++count > waits)
            {
                stop.Cancel();
                throw new OperationCanceledException(stop.Token);
            }

            clock?.Advance(step);
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
    public async Task A_store_that_stops_growing_once_it_is_as_big_as_expected_is_reported_as_finishing()
    {
        var log = new ProgressLog();
        using var stop = new CancellationTokenSource();

        var clock = new ManualTimeProvider();

        await StagingByteSampler.RunAsync(Measures(0, 5000, 5000, 5000, 5000), ReporterOver(log, 1000, clock), WaitsThenStops(4, stop, clock, TimeSpan.FromSeconds(4)), stop.Token);

        Assert.Contains(log.Reports, report => report.Finishing);
        var finishing = log.Reports.First(report => report.Finishing);
        Assert.True(finishing.BytesDone < finishing.BytesTotal, "the figure must stay short of the total until the capture is done");
    }

    [Fact]
    public async Task A_store_that_holds_still_for_less_than_the_window_is_not_finishing()
    {
        var log = new ProgressLog();
        using var stop = new CancellationTokenSource();
        var clock = new ManualTimeProvider();

        // Grew at 4 s, still at 8 s: 4 s of stillness, where git may be hashing one big file before it writes it.
        await StagingByteSampler.RunAsync(Measures(0, 5000, 5000, 5000), ReporterOver(log, 1000, clock), WaitsThenStops(2, stop, clock, TimeSpan.FromSeconds(4)), stop.Token);

        Assert.DoesNotContain(log.Reports, report => report.Finishing);
    }

    [Fact]
    public async Task A_store_still_growing_is_not_finishing()
    {
        var log = new ProgressLog();
        using var stop = new CancellationTokenSource();

        var clock = new ManualTimeProvider();

        await StagingByteSampler.RunAsync(Measures(0, 995, 996, 997, 998, 999), ReporterOver(log, 1000, clock), WaitsThenStops(5, stop, clock, TimeSpan.FromSeconds(4)), stop.Token);

        Assert.DoesNotContain(log.Reports, report => report.Finishing);
    }

    [Fact]
    public async Task A_store_that_stops_short_of_the_expected_size_is_not_finishing()
    {
        var log = new ProgressLog();
        using var stop = new CancellationTokenSource();

        var clock = new ManualTimeProvider();

        await StagingByteSampler.RunAsync(Measures(0, 400, 400, 400, 400), ReporterOver(log, 1000, clock), WaitsThenStops(4, stop, clock, TimeSpan.FromSeconds(4)), stop.Token);

        Assert.DoesNotContain(log.Reports, report => report.Finishing);
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
