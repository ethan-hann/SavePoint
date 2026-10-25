namespace Savepoint.Core.Git;

// How far apart git's file lists may fall: git prints a file's name only when its whole add ends, so shorter adds mean names sooner,
// and every add costs a process start of about 0.3 s. These are the sizes where that cost stays near 2% of a multi-minute capture.
internal sealed record StagingLimits(long BatchBytes, int BatchFiles, long SoloBytes)
{
    public static StagingLimits Default { get; } = new(2L * 1024 * 1024 * 1024, 2000, 1024L * 1024 * 1024);
}

// One add: the files it takes, their size, and whether it is a single file big enough to be worth naming while it is stored.
internal sealed record StagingBatch(IReadOnlyList<string> Files, long Bytes, bool Solo);

internal static class StagingBatches
{
    // Splits the files, in the order given, into adds. A file of SoloBytes or more is added by itself, so that for as long as it takes
    // the one file git is working on is known; the rest are grouped until the next file would pass BatchBytes or BatchFiles.
    public static IReadOnlyList<StagingBatch> Plan(IReadOnlyList<string> files, IReadOnlyList<long> sizes, StagingLimits limits)
    {
        var batches = new List<StagingBatch>();
        var group = new List<string>();
        var groupBytes = 0L;

        void Close()
        {
            if (group.Count > 0)
            {
                batches.Add(new StagingBatch(group, groupBytes, false));
                group = [];
                groupBytes = 0;
            }
        }

        for (var i = 0; i < files.Count; i++)
        {
            var size = sizes[i];
            if (size >= limits.SoloBytes)
            {
                Close();
                batches.Add(new StagingBatch([files[i]], size, true));
                continue;
            }

            if (group.Count > 0 && (group.Count >= limits.BatchFiles || groupBytes + size > limits.BatchBytes))
            {
                Close();
            }

            group.Add(files[i]);
            groupBytes += size;
        }

        Close();
        return batches;
    }
}
