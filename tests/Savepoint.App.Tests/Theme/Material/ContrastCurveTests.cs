using Savepoint.App.Theme.Material;

namespace Savepoint.App.Tests.Theme.Material;

public class ContrastCurveTests
{
    [Theory]
    [InlineData(-2.0, 1.0)]
    [InlineData(-1.0, 1.0)]
    [InlineData(-0.5, 2.0)]
    [InlineData(0.0, 3.0)]
    [InlineData(0.25, 4.0)]
    [InlineData(0.5, 5.0)]
    [InlineData(0.75, 6.0)]
    [InlineData(1.0, 7.0)]
    [InlineData(2.0, 7.0)]
    public void Get_interpolates_linearly_between_the_four_anchor_levels(double level, double expected)
    {
        var curve = new ContrastCurve(1.0, 3.0, 5.0, 7.0);

        Assert.Equal(expected, curve.Get(level));
    }
}
