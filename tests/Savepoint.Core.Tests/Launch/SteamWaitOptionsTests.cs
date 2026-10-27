using Savepoint.Core.Launch;

namespace Savepoint.Core.Tests.Launch;

public class SteamWaitOptionsTests
{
    [Fact]
    public void The_defaults_wait_ninety_seconds_and_look_every_second()
    {
        var options = new SteamWaitOptions();

        options.EnsureValid();

        Assert.Equal(TimeSpan.FromSeconds(90), options.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(1), options.PollInterval);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_polling_interval_that_is_not_positive_is_refused(int milliseconds)
    {
        var options = new SteamWaitOptions { PollInterval = TimeSpan.FromMilliseconds(milliseconds) };

        Assert.Throws<ArgumentOutOfRangeException>(options.EnsureValid);
    }

    [Fact]
    public void A_negative_timeout_is_refused_and_no_timeout_is_not()
    {
        Assert.Throws<ArgumentOutOfRangeException>(new SteamWaitOptions { Timeout = TimeSpan.FromSeconds(-1) }.EnsureValid);

        new SteamWaitOptions { Timeout = TimeSpan.Zero }.EnsureValid();
    }
}
