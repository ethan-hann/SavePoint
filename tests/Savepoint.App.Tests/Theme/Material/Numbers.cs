namespace Savepoint.App.Tests.Theme.Material;

internal static class Numbers
{
    // V8 and the platform libm differ in the last bits of atan2 and pow; ARGB results are still compared exactly.
    private static readonly double _hctTolerance = 1e-10;

    public static void AssertSame(double expected, double actual, string what) =>
        Assert.True(expected == actual, $"{what}: expected {expected:R}, actual {actual:R}");

    public static void AssertClose(double expected, double actual, string what) =>
        Assert.True(
            Math.Abs(expected - actual) <= _hctTolerance,
            $"{what}: expected {expected:R}, actual {actual:R}, difference {Math.Abs(expected - actual):R}");
}
