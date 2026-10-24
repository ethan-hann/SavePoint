using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Localization;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Common;

public class ProgressWordingTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    [Fact]
    public void Files_are_counted_with_grouping_and_the_right_plural()
    {
        Assert.Equal("1,200 of 3,430 files", _text.FilesOf(1200, 3430));
        Assert.Equal("1 of 1 file", _text.FilesOf(1, 1));
    }

    [Fact]
    public void Bytes_are_sizes()
    {
        Assert.Equal("3.1 GB of 70 GB", _text.BytesOf(3_100_000_000, 70_000_000_000));
    }

    [Fact]
    public void An_unknown_total_is_said_in_plain_words()
    {
        Assert.Equal("Working out how much there is to copy…", _text.Counting());
    }

    [Fact]
    public void A_job_that_is_wrapping_up_says_so_instead_of_showing_a_figure_that_has_stopped()
    {
        var wrapping = new JobProgress(4_996, 5_068, 90_500_000_000, 91_400_000_000, null, Finishing: true);

        Assert.Equal("Making sure everything is stored safely. This can take a few minutes for a big game.", _text.Describe(wrapping));
        Assert.Equal("90.5 GB of 91.4 GB", _text.Describe(wrapping with { Finishing = false }));
    }

    [Fact]
    public void Bytes_set_the_fraction_when_both_are_known()
    {
        Assert.Equal(0.5, ProgressWording.Fraction(new JobProgress(22, 23, 35, 70)));
    }

    [Fact]
    public void Files_set_the_fraction_when_there_are_no_bytes()
    {
        Assert.Equal(0.25, ProgressWording.Fraction(new JobProgress(1, 4)));
    }

    [Fact]
    public void The_fraction_is_unknown_without_a_total_and_never_leaves_zero_to_one()
    {
        Assert.Null(ProgressWording.Fraction(new JobProgress(FilesDone: 5)));
        Assert.Null(ProgressWording.Fraction(JobProgress.None));
        Assert.Equal(1.0, ProgressWording.Fraction(new JobProgress(BytesDone: 90, BytesTotal: 80)));
    }
}
