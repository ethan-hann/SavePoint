namespace Savepoint.Core.Git;

// Watches the object store while git stages files and tells the reporter how many bytes it has written.
internal static class StagingByteSampler
{
    // The growth since the first look is the work done. A last look is taken when the sampler is stopped, so the final bytes are never missed.
    public static async Task RunAsync(Func<long> measure, ProgressReporter reporter, Func<CancellationToken, Task> wait, CancellationToken ct)
    {
        var baseline = Measure(measure);
        while (true)
        {
            try
            {
                await wait(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            Sample(measure, baseline, reporter);
        }

        Sample(measure, baseline, reporter);
    }

    private static void Sample(Func<long> measure, long? baseline, ProgressReporter reporter)
    {
        if (baseline is not { } start || Measure(measure) is not { } now)
        {
            return;
        }

        reporter.ObserveWritten(Math.Max(now - start, 0));
    }

    // Looking is best effort: a file that vanishes mid-count must not end the capture.
    private static long? Measure(Func<long> measure)
    {
        try
        {
            return measure();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
