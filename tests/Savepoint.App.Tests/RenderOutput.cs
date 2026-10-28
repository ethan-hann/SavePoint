namespace Savepoint.App.Tests;

// The PNGs the render tests write to render-output/ are for a person to compare by eye, so they are only drawn when asked:
// SAVEPOINT_RENDER=1 dotnet test --project tests/Savepoint.App.Tests -c Release --filter-class <the render test class>. The gate skips them.
internal static class RenderOutput
{
    public static bool Requested => Environment.GetEnvironmentVariable("SAVEPOINT_RENDER") == "1";

    public static void SkipUnlessRequested() =>
        Assert.SkipUnless(Requested, "Set SAVEPOINT_RENDER=1 to write the PNGs to render-output/.");
}
