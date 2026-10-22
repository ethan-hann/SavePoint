using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

public sealed class CheckoutBytePlanTests
{
    private static GitEngine.TreeChange Change(char status, string path, string newId = "") => new(status, "old-" + path, newId, path);

    private static CheckoutBytePlan Plan() =>
        new(
            [Change('M', "b/second", "id-second"), Change('D', "z/gone"), Change('A', "a/first", "id-first")],
            new Dictionary<string, long> { ["id-first"] = 400, ["id-second"] = 600 });

    [Fact]
    public void Removed_files_come_first_and_written_files_follow_in_path_order()
    {
        var plan = Plan();

        Assert.Equal(3, plan.Entries);
        Assert.Equal(1000, plan.Total);
        Assert.Equal([0L, 0L, 400L, 1000L], new[] { 0, 1, 2, 3 }.Select(done => plan.BytesAfter(done, 3)));
    }

    [Fact]
    public void A_count_beyond_the_total_stops_at_the_total()
    {
        Assert.Equal(1000, Plan().BytesAfter(9, 3));
    }

    [Fact]
    public void When_git_counts_a_different_number_of_files_the_bytes_are_spread_over_its_count()
    {
        var plan = Plan();

        Assert.Equal(500, plan.BytesAfter(2, 4));
        Assert.Equal(1000, plan.BytesAfter(4, 4));
    }

    [Fact]
    public void A_plan_with_nothing_to_write_has_no_bytes()
    {
        var plan = new CheckoutBytePlan([], new Dictionary<string, long>());

        Assert.Equal(0, plan.Total);
        Assert.Equal(0, plan.BytesAfter(0, 0));
    }
}
