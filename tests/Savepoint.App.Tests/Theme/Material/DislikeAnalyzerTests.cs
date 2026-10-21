using Savepoint.App.Theme.Material;

namespace Savepoint.App.Tests.Theme.Material;

public class DislikeAnalyzerTests
{
    public static TheoryData<string, bool, string> Samples()
    {
        var data = new TheoryData<string, bool, string>();
        foreach (var sample in GoldenData.Current.Dislike)
        {
            data.Add(sample.Argb, sample.Disliked, sample.Fixed);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Matches_the_reference_for_colors_around_the_dark_yellow_green_zone(string argb, bool disliked, string fixedArgb)
    {
        var hct = Hct.FromInt(GoldenData.ParseArgb(argb));

        Assert.Equal(disliked, DislikeAnalyzer.IsDisliked(hct));
        Assert.Equal(fixedArgb, GoldenData.Hex(DislikeAnalyzer.FixIfDisliked(hct).ToInt()));
    }

    [Theory]
    [InlineData(100.0, 16.5, 50.0, true)]
    [InlineData(100.0, 16.49, 50.0, false)]
    [InlineData(100.0, 20.0, 64.5, false)]
    [InlineData(100.0, 20.0, 64.49, true)]
    [InlineData(89.49, 20.0, 50.0, false)]
    [InlineData(89.5, 20.0, 50.0, true)]
    [InlineData(111.49, 20.0, 50.0, true)]
    [InlineData(111.5, 20.0, 50.0, false)]
    public void Rounds_each_coordinate_half_up_before_comparing(double hue, double chroma, double tone, bool expected)
    {
        Assert.Equal(expected, DislikeAnalyzer.IsDisliked(hue, chroma, tone));
    }

    [Fact]
    public void An_acceptable_color_is_returned_unchanged()
    {
        var blue = Hct.FromInt(0xFF2986CC);

        Assert.Same(blue, DislikeAnalyzer.FixIfDisliked(blue));
    }
}
