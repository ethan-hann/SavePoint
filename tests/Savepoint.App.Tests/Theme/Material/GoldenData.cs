using System.Globalization;
using System.Text.Json;

namespace Savepoint.App.Tests.Theme.Material;

internal sealed record GoldenFile(
    string[] Roles,
    Dictionary<string, string> Seeds,
    GoldenViewingConditions ViewingConditions,
    GoldenScheme[] Schemes,
    GoldenHctFromArgb[] HctFromArgb,
    GoldenHctSolved[] HctSolved,
    GoldenTonalPalette[] TonalPalettes,
    GoldenTonalFromInt[] TonalFromInt,
    GoldenDislike[] Dislike,
    GoldenContrast Contrast);

internal sealed record GoldenViewingConditions(
    double N, double Aw, double Nbb, double Ncb, double C, double Nc, double[] RgbD, double Fl, double FLRoot, double Z);

internal sealed record GoldenPalette(double Hue, double Chroma, string KeyColor);

internal sealed record GoldenScheme(
    string Id,
    string Group,
    string Variant,
    string Seed,
    string SourceColor,
    bool Dark,
    double Contrast,
    Dictionary<string, GoldenPalette> Palettes,
    Dictionary<string, string> Colors);

internal sealed record GoldenHctFromArgb(string Argb, double Hue, double Chroma, double Tone);

internal sealed record GoldenHctSolved(
    double Hue, double Chroma, double Tone, string Argb, double OutHue, double OutChroma, double OutTone);

internal sealed record GoldenTonalPalette(
    double Hue,
    double Chroma,
    string KeyColor,
    double KeyHue,
    double KeyChroma,
    double KeyTone,
    Dictionary<string, string> Tones);

internal sealed record GoldenTonalFromInt(
    string Seed, double Hue, double Chroma, string KeyColor, Dictionary<string, string> Tones);

internal sealed record GoldenDislike(string Argb, bool Disliked, string Fixed);

internal sealed record GoldenRatio(double A, double B, double Ratio);

internal sealed record GoldenContrastResult(double Tone, double Ratio, double Result);

internal sealed record GoldenContrast(GoldenRatio[] Ratios, GoldenContrastResult[] Lighter, GoldenContrastResult[] Darker);

internal static class GoldenData
{
    private static readonly Lazy<GoldenFile> _file = new(Read);

    public static GoldenFile Current => _file.Value;

    public static uint ParseArgb(string hex) =>
        uint.Parse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    public static string Hex(uint argb) => "#" + argb.ToString("X8", CultureInfo.InvariantCulture);

    private static GoldenFile Read()
    {
        const string resourceName = "golden-mcu-0.3.0.json";
        using var stream = typeof(GoldenData).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource {resourceName} is missing.");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return JsonSerializer.Deserialize<GoldenFile>(stream, options)
            ?? throw new InvalidOperationException("The golden file is empty.");
    }
}
