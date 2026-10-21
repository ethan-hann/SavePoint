namespace Savepoint.Tools.IconGen;

// The light theme's primary and onPrimary for the default blue accent, as the reference color utilities compute them
// (tonalSpot-blue-light-c0 in tests/Savepoint.App.Tests/Theme/Material/golden-mcu-0.3.0.json). A test compares them with the app's palette.
internal static class IconColors
{
    internal const uint Primary = 0xFF31628D;

    internal const uint OnPrimary = 0xFFFFFFFF;
}
