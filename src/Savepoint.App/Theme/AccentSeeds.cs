using Savepoint.Core.Catalog;

namespace Savepoint.App.Theme;

/// <summary>The five accent presets from the visual spec; every other color is derived from the chosen seed.</summary>
public static class AccentSeeds
{
    /// <summary>The preset's seed as an opaque ARGB value.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined accent.</exception>
    public static uint For(AccentColorId accent) => accent switch
    {
        AccentColorId.Blue => 0xFF2986CC,
        AccentColorId.Violet => 0xFF7C4DFF,
        AccentColorId.Green => 0xFF2E7D32,
        AccentColorId.Amber => 0xFFF9A825,
        AccentColorId.Crimson => 0xFFC2185B,
        _ => throw new ArgumentOutOfRangeException(nameof(accent), accent, null),
    };
}
