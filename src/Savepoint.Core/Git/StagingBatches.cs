namespace Savepoint.Core.Git;

// How far apart git's file lists may fall: git prints a file's name only when its whole add ends, so shorter adds mean names sooner,
// and every add costs a process start of about 0.3 s. These are the sizes where that cost stays near 2% of a multi-minute capture.
// BigBytes is git's core.bigFileThreshold: a file over it is streamed into a pack, and every add that streams writes a pack of its own.
internal sealed record StagingLimits(long BatchBytes, int BatchFiles, long BigBytes)
{
    public static StagingLimits Default { get; } = new(2L * 1024 * 1024 * 1024, 2000, 128L * 1024 * 1024);
}

// One add: the files it takes and their sizes, and whether it holds the files git streams into a pack.
internal sealed record StagingBatch(IReadOnlyList<string> Files, IReadOnlyList<long> Sizes, bool Big)
{
    public long Bytes => Sizes.Sum();
}

internal static class StagingBatches
{
    // git's add takes the tracked files that changed first, in index order, and then the new ones in path order; the listing has
    // the new ones first. The order matters to the estimate of which big file is being stored.
    public static StagingBatch AsGitTakesThem(StagingBatch batch, Func<string, bool> isTracked)
    {
        var order = Enumerable.Range(0, batch.Files.Count).OrderBy(i => isTracked(batch.Files[i]) ? 0 : 1).ToList();
        return new StagingBatch([.. order.Select(i => batch.Files[i])], [.. order.Select(i => batch.Sizes[i])], batch.Big);
    }

    public static IReadOnlyList<StagingBatch> Plan(IReadOnlyList<string> files, IReadOnlyList<long> sizes, StagingLimits limits)
    {
        var batches = new List<StagingBatch>();
        var group = new List<string>();
        var groupSizes = new List<long>();
        var groupBytes = 0L;
        var bigFiles = new List<string>();
        var bigSizes = new List<long>();

        void Close()
        {
            if (group.Count > 0)
            {
                batches.Add(new StagingBatch(group, groupSizes, false));
                group = [];
                groupSizes = [];
                groupBytes = 0;
            }
        }

        for (var i = 0; i < files.Count; i++)
        {
            var size = sizes[i];
            if (size >= limits.BigBytes)
            {
                bigFiles.Add(files[i]);
                bigSizes.Add(size);
                continue;
            }

            if (group.Count > 0 && (group.Count >= limits.BatchFiles || groupBytes + size > limits.BatchBytes))
            {
                Close();
            }

            group.Add(files[i]);
            groupSizes.Add(size);
            groupBytes += size;
        }

        Close();

        // One add, so the capture writes one pack for all of them and not one per file; the routine repack weighs packs by object count
        // and would rewrite a pack of one big archive along with the rest.
        if (bigFiles.Count > 0)
        {
            batches.Add(new StagingBatch(bigFiles, bigSizes, true));
        }

        return batches;
    }
}
