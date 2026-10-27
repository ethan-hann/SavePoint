namespace Savepoint.App.Theme;

/// <summary>The steps the interface can be scaled to, on top of the operating system's own display scaling.</summary>
public static class UiScale
{
    /// <summary>The scale that leaves the interface as designed.</summary>
    public const int Default = 100;

    /// <summary>The offered steps in percent, smallest first.</summary>
    public static IReadOnlyList<int> Steps { get; } = [100, 125, 150, 175, 200];

    /// <summary>The step closest to a stored percent; the lower step when it is exactly between two.</summary>
    /// <param name="percent">Any number, such as one typed into a settings file by hand.</param>
    public static int Nearest(int percent) =>
        Steps.OrderBy(step => Math.Abs(step - percent)).ThenBy(step => step).First();
}
