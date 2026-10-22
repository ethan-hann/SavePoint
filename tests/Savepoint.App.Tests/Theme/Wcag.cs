namespace Savepoint.App.Tests.Theme;

// WCAG 2.x contrast, computed straight from the colors so a test never trusts a tone label.
internal static class Wcag
{
    /// <summary>The contrast ratio of two opaque colors, 1 to 21.</summary>
    public static double Ratio(uint first, uint second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(uint argb) =>
        (0.2126 * Linear((argb >> 16) & 0xFF)) + (0.7152 * Linear((argb >> 8) & 0xFF)) + (0.0722 * Linear(argb & 0xFF));

    private static double Linear(uint channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
