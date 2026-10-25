using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

public sealed class StoreMeterTests
{
    [Fact]
    public void Growth_is_what_the_store_gained_since_the_meter_was_made()
    {
        var size = 100L;
        var meter = new StoreMeter(() => size);

        size = 340;

        Assert.Equal(240, meter.Growth());
    }

    [Fact]
    public void A_store_that_shrank_has_not_grown()
    {
        var size = 100L;
        var meter = new StoreMeter(() => size);

        size = 40;

        Assert.Equal(0, meter.Growth());
    }

    [Fact]
    public void A_look_that_fails_gives_no_answer_and_does_not_throw()
    {
        var fail = false;
        var meter = new StoreMeter(() => fail ? throw new IOException("gone") : 10);

        fail = true;

        Assert.Null(meter.Growth());
    }

    [Fact]
    public void A_meter_made_while_the_store_cannot_be_read_never_answers()
    {
        var meter = new StoreMeter(() => throw new UnauthorizedAccessException());

        Assert.Null(meter.Growth());
    }
}
