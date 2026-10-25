namespace Savepoint.Core.Git;

// Watches the object store while git stages files and tells the reporter how many bytes it has written.
internal static class StagingByteSampler
{
    public static Task RunAsync(Func<long> measure, ProgressReporter reporter, Func<CancellationToken, Task> wait, CancellationToken ct) =>
        RunAsync(new StoreMeter(measure), reporter, wait, ct);

    // The growth since the meter was made is the work done. A last look is taken when the sampler is stopped, so the final bytes are never missed.
    public static async Task RunAsync(StoreMeter meter, ProgressReporter reporter, Func<CancellationToken, Task> wait, CancellationToken ct)
    {
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

            Sample(meter, reporter);
        }

        Sample(meter, reporter);
    }

    private static void Sample(StoreMeter meter, ProgressReporter reporter)
    {
        if (meter.Growth() is { } written)
        {
            reporter.ObserveWritten(written);
        }
    }
}
