using Savepoint.App.Features.Launch;

namespace Savepoint.App.Tests.Features.Launch;

public class LaunchTimingTests
{
    [Fact]
    public void Estimating_six_gigabytes_takes_about_two_minutes_at_the_assumed_rate()
    {
        Assert.Equal(TimeSpan.FromSeconds(122), LaunchTiming.Estimate(6_100_000_000));
    }

    [Fact]
    public void Estimating_nothing_takes_no_time()
    {
        Assert.Equal(TimeSpan.Zero, LaunchTiming.Estimate(0));
        Assert.Equal(TimeSpan.Zero, LaunchTiming.Estimate(-5));
    }

    [Fact]
    public void Before_a_rate_can_be_measured_the_time_left_uses_the_assumed_rate()
    {
        var left = LaunchTiming.Remaining(3_800_000_000, 6_100_000_000, TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(46), left);
    }

    [Fact]
    public void After_the_measure_window_the_time_left_uses_the_measured_rate()
    {
        // 1 GB in 10 seconds is 100 MB/s, so the remaining 5.1 GB takes 51 seconds.
        var left = LaunchTiming.Remaining(1_000_000_000, 6_100_000_000, TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(51), left);
    }

    [Fact]
    public void With_nothing_done_yet_the_measured_rate_is_not_used()
    {
        var left = LaunchTiming.Remaining(0, 1_000_000_000, TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(20), left);
    }

    [Theory]
    [InlineData(100L, 0L)]
    [InlineData(100L, -1L)]
    [InlineData(100L, 100L)]
    [InlineData(200L, 100L)]
    public void There_is_no_time_left_without_a_total_or_when_everything_is_done(long done, long total)
    {
        Assert.Null(LaunchTiming.Remaining(done, total, TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData(46, 50)]
    [InlineData(3, 10)]
    [InlineData(0, 10)]
    [InlineData(54, 50)]
    [InlineData(56, 60)]
    [InlineData(90, 120)]
    [InlineData(100, 120)]
    [InlineData(61, 60)]
    [InlineData(3600, 3600)]
    public void Display_rounding_snaps_to_tens_of_seconds_then_whole_minutes(int seconds, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), LaunchTiming.RoundForDisplay(TimeSpan.FromSeconds(seconds)));
    }
}
