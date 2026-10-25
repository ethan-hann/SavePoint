namespace Savepoint.Core.Git;

// How much the object store has grown since this meter was made. Looking is best effort: a file that vanishes mid-count is no answer, not a failure.
internal sealed class StoreMeter(Func<long> measure)
{
    private readonly long? _baseline = Take(measure);

    public long? Growth() => _baseline is { } start && Take(measure) is { } now ? Math.Max(now - start, 0) : null;

    internal static long? Take(Func<long> measure)
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
