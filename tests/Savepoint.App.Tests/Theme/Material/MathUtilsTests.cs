using Savepoint.App.Theme.Material;

namespace Savepoint.App.Tests.Theme.Material;

public class MathUtilsTests
{
    [Theory]
    [InlineData(0.5, 1.0)]
    [InlineData(1.5, 2.0)]
    [InlineData(2.5, 3.0)]
    [InlineData(-0.5, 0.0)]
    [InlineData(-1.5, -1.0)]
    [InlineData(-2.5, -2.0)]
    [InlineData(2.4, 2.0)]
    [InlineData(-2.6, -3.0)]
    [InlineData(0.49999999999999994, 0.0)]
    public void JsRound_rounds_halves_toward_positive_infinity(double input, double expected)
    {
        Assert.Equal(expected, MathUtils.JsRound(input));
    }

    [Theory]
    [InlineData(-30.0, 330.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(360.0, 0.0)]
    [InlineData(725.0, 5.0)]
    [InlineData(359.5, 359.5)]
    [InlineData(-720.0, 0.0)]
    public void SanitizeDegreesDouble_wraps_into_zero_to_360(double input, double expected)
    {
        Assert.Equal(expected, MathUtils.SanitizeDegreesDouble(input));
    }

    [Theory]
    [InlineData(-5.0, -1.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(5.0, 1.0)]
    public void Signum_returns_the_sign(double input, double expected)
    {
        Assert.Equal(expected, MathUtils.Signum(input));
    }

    [Theory]
    [InlineData(0.0, 10.0, 20.0, 10.0)]
    [InlineData(1.0, 10.0, 20.0, 20.0)]
    [InlineData(0.25, 10.0, 20.0, 12.5)]
    public void Lerp_interpolates_between_start_and_stop(double amount, double start, double stop, double expected)
    {
        Assert.Equal(expected, MathUtils.Lerp(start, stop, amount));
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(101.0, 100.0)]
    [InlineData(42.5, 42.5)]
    public void ClampDouble_limits_to_the_range(double input, double expected)
    {
        Assert.Equal(expected, MathUtils.ClampDouble(0.0, 100.0, input));
    }

    [Fact]
    public void MatrixMultiply_multiplies_a_row_by_a_matrix()
    {
        double[][] matrix = [[1, 2, 3], [4, 5, 6], [7, 8, 9]];

        var product = MathUtils.MatrixMultiply([1, 10, 100], matrix);

        Assert.Equal([321.0, 654.0, 987.0], product);
    }
}
