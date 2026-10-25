using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

// A capture hands git its files in groups, so the names of one group are printed when that group ends. Every file git streams into a
// pack goes in one group, because each add that streams writes a pack of its own.
public sealed class StagingBatchesTests
{
    private static readonly StagingLimits _limits = new(BatchBytes: 100, BatchFiles: 3, BigBytes: 60);

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
    public void Every_big_file_is_in_one_batch_that_comes_last()
    {
        var batches = Plan(10, 60, 10, 500, 10, 61);

        Assert.Equal([["f0", "f2", "f4"], ["f1", "f3", "f5"]], Names(batches));
        Assert.Equal([false, true], batches.Select(batch => batch.Big));
        Assert.Equal([60L, 500L, 61L], batches[1].Sizes);
    }

    [Fact]
    public void A_big_batch_ignores_the_file_and_byte_limits()
    {
        var batches = Plan(200, 200, 200, 200, 200);

        Assert.Single(batches);
        Assert.True(batches[0].Big);
        Assert.Equal(1000, batches[0].Bytes);
    }

    [Fact]
    public void A_file_just_under_the_big_size_is_grouped_with_the_small_ones()
    {
        var batches = Plan(59, 30);

        Assert.Equal([["f0", "f1"]], Names(batches));
        Assert.False(batches[0].Big);
    }

    [Fact]
    public void Every_file_lands_in_exactly_one_batch()
    {
        var batches = Plan(70, 1, 99, 2, 2, 2, 2, 61, 100, 5);

        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"f{i}").Order(StringComparer.Ordinal), batches.SelectMany(batch => batch.Files).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Small_files_keep_the_order_given()
    {
        var batches = Plan(1, 70, 2, 3, 4);

        Assert.Equal(["f0", "f2", "f3", "f4"], batches.Where(batch => !batch.Big).SelectMany(batch => batch.Files));
    }

    [Fact]
    public void Git_takes_the_files_it_already_tracks_before_the_new_ones_and_each_group_keeps_its_order()
    {
        var batch = new StagingBatch(["a", "b", "c", "d", "e"], [1, 2, 3, 4, 5], true);

        var ordered = StagingBatches.AsGitTakesThem(batch, path => path is "c" or "e");

        Assert.Equal(["c", "e", "a", "b", "d"], ordered.Files);
        Assert.Equal([3L, 5L, 1L, 2L, 4L], ordered.Sizes);
        Assert.True(ordered.Big);
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

        Assert.Equal([70L, 70L], batches.Select(batch => batch.Bytes).Order());
    }
}
