using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Localization;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Common;

public class UiFormatterTests
{
    private static readonly DateTimeOffset _now = new(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo _centralDaylight = TimeZoneInfo.CreateCustomTimeZone("test-cdt", TimeSpan.FromHours(-5), "CDT", "CDT");

    private static UiFormatter English(string os = "windows", TimeZoneInfo? timeZone = null) =>
        new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), new FixedClock(_now), os, timeZone ?? TimeZoneInfo.Utc);

    [Theory]
    [InlineData(0L, "0 bytes")]
    [InlineData(1L, "1 byte")]
    [InlineData(512L, "512 bytes")]
    [InlineData(999L, "999 bytes")]
    [InlineData(1_000L, "1 KB")]
    [InlineData(1_500L, "1.5 KB")]
    [InlineData(2_400_000L, "2.4 MB")]
    [InlineData(6_100_000_000L, "6.1 GB")]
    [InlineData(41_200_000_000L, "41.2 GB")]
    [InlineData(131_000_000_000L, "131 GB")]
    [InlineData(210_000_000_000L, "210 GB")]
    [InlineData(1_200_000_000_000L, "1.2 TB")]
    [InlineData(999_950_000L, "1 GB")]
    [InlineData(-5L, "0 bytes")]
    public void File_sizes_use_decimal_units_with_one_fraction_digit_and_no_trailing_zero(long bytes, string expected)
    {
        Assert.Equal(expected, English().FileSize(bytes));
    }

    [Fact]
    public void File_sizes_use_the_decimal_separator_of_the_catalog_language()
    {
        var catalog = LocalizationCatalog.FromJson("""{ "format.size.gb": "{value} Go" }""", "fr", throwOnMissingKey: true);

        Assert.Equal("41,2 Go", new UiFormatter(catalog, new FixedClock(_now), "windows", TimeZoneInfo.Utc).FileSize(41_200_000_000));
    }

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(999L, "999")]
    [InlineData(1_234L, "1,234")]
    [InlineData(1_234_567L, "1,234,567")]
    public void Counts_use_the_group_separator_of_the_catalog_language(long value, string expected)
    {
        Assert.Equal(expected, English().Number(value));
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(30, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1 minute ago")]
    [InlineData(5 * 60, "5 minutes ago")]
    [InlineData((59 * 60) + 59, "59 minutes ago")]
    [InlineData(60 * 60, "1 hour ago")]
    [InlineData((23 * 3600) + (59 * 60), "23 hours ago")]
    [InlineData(24 * 3600, "1 day ago")]
    [InlineData(3 * 86_400, "3 days ago")]
    [InlineData((6 * 86_400) + (23 * 3600), "6 days ago")]
    [InlineData(7 * 86_400, "1 week ago")]
    [InlineData(42 * 86_400, "6 weeks ago")]
    [InlineData(84 * 86_400, "12 weeks ago")]
    [InlineData(91 * 86_400, "3 months ago")]
    [InlineData(200 * 86_400, "7 months ago")]
    [InlineData(365 * 86_400, "1 year ago")]
    [InlineData(800 * 86_400, "2 years ago")]
    [InlineData(-300, "just now")]
    public void Relative_times_pick_the_largest_unit_that_fits(int secondsAgo, string expected)
    {
        var when = _now - TimeSpan.FromSeconds(secondsAgo);

        Assert.Equal(expected, English().RelativeTime(when));
    }

    [Fact]
    public void Relative_time_is_measured_against_the_injected_clock()
    {
        var clock = new FixedClock(_now);
        var formatter = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), clock, "windows", TimeZoneInfo.Utc);
        var when = _now - TimeSpan.FromHours(2);

        Assert.Equal("2 hours ago", formatter.RelativeTime(when));

        clock.Now += TimeSpan.FromHours(24);

        Assert.Equal("1 day ago", formatter.RelativeTime(when));
    }

    [Fact]
    public void Dates_and_times_follow_the_catalog_pattern_in_the_formatters_time_zone()
    {
        var captured = new DateTimeOffset(2026, 7, 2, 22, 40, 0, TimeSpan.Zero);
        var utc = English(timeZone: TimeZoneInfo.Utc);
        var central = English(timeZone: _centralDaylight);

        Assert.Equal("Jul 2, 2026", utc.Date(captured));
        Assert.Equal("10:40 PM", utc.Time(captured));
        Assert.Equal("Jul 2, 2026, 10:40 PM", utc.DateAndTime(captured));
        Assert.Equal("5:40 PM", central.Time(captured));
        Assert.Equal("Jul 2, 2026, 5:40 PM", central.DateAndTime(captured));
    }

    [Fact]
    public void A_late_evening_instant_lands_on_the_next_day_in_a_zone_ahead_of_it()
    {
        var captured = new DateTimeOffset(2026, 7, 2, 23, 42, 0, TimeSpan.FromHours(-5));

        Assert.Equal("Jul 3, 2026", English(timeZone: TimeZoneInfo.Utc).Date(captured));
        Assert.Equal("Jul 2, 2026", English(timeZone: _centralDaylight).Date(captured));
    }

    [Fact]
    public void Default_names_carry_the_date_as_month_day_and_two_digit_year_without_padding()
    {
        Assert.Equal("7-2-26", English().NameDate(new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero)));
        Assert.Equal("12-25-27", English().NameDate(new DateTimeOffset(2027, 12, 25, 9, 0, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData(0, "0 seconds")]
    [InlineData(1, "1 second")]
    [InlineData(50, "50 seconds")]
    [InlineData(59, "59 seconds")]
    [InlineData(60, "1 minute")]
    [InlineData(41 * 60, "41 minutes")]
    [InlineData((59 * 60) + 59, "59 minutes")]
    [InlineData(3_600, "1 hour")]
    [InlineData(3_660, "1 hour 1 minute")]
    [InlineData((2 * 3600) + (14 * 60), "2 hours 14 minutes")]
    [InlineData((2 * 3600) + 30, "2 hours")]
    [InlineData(25 * 3600, "25 hours")]
    public void Durations_read_in_whole_units_with_plurals(int seconds, string expected)
    {
        Assert.Equal(expected, English().Duration(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void A_message_is_formatted_with_the_arguments_given_by_name()
    {
        var text = English().Message("firstRun.title");

        Assert.Equal("Keep the setup that works", text);
    }

    [Fact]
    public void An_error_message_is_formatted_with_the_arguments_the_error_carries()
    {
        var error = ErrorInfo.From(new GameRunningException("g1", "Skyrim"));

        Assert.Equal("Skyrim is running right now, so SavePoint can't do that yet. Close the game, then try again.", English().Message(error));
    }

    [Theory]
    [InlineData("windows", "a Windows folder")]
    [InlineData("macos", "a macOS system folder")]
    [InlineData("linux", "a system folder")]
    public void An_error_message_that_names_the_operating_system_follows_the_formatters_os(string os, string expected)
    {
        var error = ErrorInfo.From(new PathSafetyException("/x", 1));

        Assert.Contains(expected, English(os).Message(error));
    }

    [Fact]
    public void The_operating_system_defaults_to_the_one_the_app_runs_on()
    {
        var formatter = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true));

        Assert.Contains(formatter.Os, new[] { "windows", "macos", "linux" });
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
