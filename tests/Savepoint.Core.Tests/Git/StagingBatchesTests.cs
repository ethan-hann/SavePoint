using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

// A capture hands git its files in groups, so the names of one group are printed when that group ends.
public sealed class StagingBatchesTests
{
    private static readonly StagingLimits _limits = new(BatchBytes: 100, BatchFiles: 3, SoloBytes: 60);

    private static IReadOnlyList<StagingBatch> Plan(params long[] sizes) =>
        StagingBatches.Plan([.. sizes.Select((_, i) => $"f{i}")], sizes, _limits);

    private static string[][] Names(IReadOnlyList<StagingBatch> batches) => [.. batches.Select(batch => batch.Files.ToArray())];

    [Fact]
    public void Files_that_fit_one_batch_stay_one_batch()
    {
        var batches = Plan(10, 20, 30);

        Assert.Equal([["f0", "f1", "f2"]], Names(batches));
        Assert.Equal(60, batches[0].Bytes);
    }

    [Fact]
    public void A_batch_closes_when_the_next_file_would_pass_the_byte_limit()
    {
        var batches = Plan(50, 40, 20, 5);

        Assert.Equal([["f0", "f1"], ["f2", "f3"]], Names(batches));
    }

    [Fact]
    public void A_batch_closes_at_the_file_limit()
    {
        var batches = Plan(1, 1, 1, 1, 1, 1, 1);

        Assert.Equal([["f0", "f1", "f2"], ["f3", "f4", "f5"], ["f6"]], Names(batches));
    }

    [Fact]
    public void A_large_file_is_a_batch_of_its_own_and_is_marked_solo()
    {
        var batches = Plan(10, 60, 10, 500, 10);

        Assert.Equal([["f0"], ["f1"], ["f2"], ["f3"], ["f4"]], Names(batches));
        Assert.Equal([false, true, false, true, false], batches.Select(batch => batch.Solo));
    }

    [Fact]
    public void A_file_just_under_the_solo_size_is_grouped_with_others()
    {
        var batches = Plan(59, 30);

        Assert.Equal([["f0", "f1"]], Names(batches));
        Assert.False(batches[0].Solo);
    }

    [Fact]
    public void Every_file_lands_in_exactly_one_batch_in_the_order_given()
    {
        var batches = Plan(70, 1, 99, 2, 2, 2, 2, 61, 100, 5);

        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"f{i}"), batches.SelectMany(batch => batch.Files));
    }

    [Fact]
    public void No_files_make_no_batches()
    {
        Assert.Empty(Plan());
    }

    [Fact]
    public void A_batch_reports_the_bytes_of_its_files()
    {
        var batches = Plan(70, 30, 40);

        Assert.Equal([70L, 70L], batches.Select(batch => batch.Bytes));
    }
}
