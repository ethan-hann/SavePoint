using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Search;
using Savepoint.App.Localization;

namespace Savepoint.App.Tests.Features.Common;

public class UiFormatterDateSearchTests
{
    private static readonly DateTimeOffset _saturdayNight = new(2026, 9, 5, 23, 42, 0, TimeSpan.FromHours(-5));

    private static UiFormatter English(TimeZoneInfo? zone = null) =>
        new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), timeZone: zone ?? TimeZoneInfo.CreateCustomTimeZone("sample", TimeSpan.FromHours(-5), "sample", "sample"));

    private static bool Finds(UiFormatter text, string typed) =>
        SearchQuery.Parse(typed).Matches(SearchTarget.Of(text.DateSearchText(_saturdayNight)));

    [Fact]
    public void A_date_is_found_by_its_year_and_month_typed_with_a_dash_or_with_the_day()
    {
        var text = English();

        Assert.True(Finds(text, "2026-09"));
        Assert.True(Finds(text, "2026-09-05"));
        Assert.True(Finds(text, "2026"));
        Assert.False(Finds(text, "2026-10"));
        Assert.False(Finds(text, "2025-09"));
    }

    [Fact]
    public void A_date_is_found_by_the_name_of_its_month_in_full_or_short_in_any_case()
    {
        var text = English();

        Assert.True(Finds(text, "september"));
        Assert.True(Finds(text, "SEP"));
        Assert.False(Finds(text, "august"));
    }

    [Fact]
    public void A_date_is_found_by_the_day_of_the_week()
    {
        Assert.True(Finds(English(), "saturday"));
        Assert.False(Finds(English(), "friday"));
    }

    [Fact]
    public void The_date_is_read_in_the_formatters_time_zone()
    {
        var utc = English(TimeZoneInfo.Utc);

        Assert.True(Finds(utc, "2026-09-06"));
        Assert.False(Finds(utc, "2026-09-05"));
    }

    [Fact]
    public void The_month_is_named_in_the_catalogs_language()
    {
        var french = new UiFormatter(LocalizationCatalog.FromJson("{}", "fr"), timeZone: TimeZoneInfo.Utc);

        Assert.True(SearchQuery.Parse("septembre").Matches(SearchTarget.Of(french.DateSearchText(_saturdayNight))));
        Assert.True(SearchQuery.Parse("dimanche").Matches(SearchTarget.Of(french.DateSearchText(_saturdayNight))));
        Assert.False(SearchQuery.Parse("september").Matches(SearchTarget.Of(french.DateSearchText(_saturdayNight))));
    }
}
