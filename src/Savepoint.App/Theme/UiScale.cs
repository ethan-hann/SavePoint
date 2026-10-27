using Avalonia;

namespace Savepoint.App.Theme;

/// <summary>The steps the interface can be scaled to, on top of the operating system's own display scaling.</summary>
public static class UiScale
{
    /// <summary>The scale that leaves the interface as designed.</summary>
    public const int Default = 100;

    /// <summary>The offered steps in percent, smallest first.</summary>
    public static IReadOnlyList<int> Steps { get; } = [100, 125, 150, 175, 200];

    /// <summary>The smallest size the screens are built for, at 100%: every screen shows all it has to in this much room (D36).</summary>
    public static Size BaseMinimum { get; } = new(640, 420);

    // The title bar and frame are not part of the client size the window's minimum applies to.
    private const double FrameAllowance = 40;

    /// <summary>The room a window's client area can have on a screen, in the window's own units.</summary>
    /// <param name="workArea">The screen's working area in device pixels: the screen less the taskbar or dock.</param>
    /// <param name="screenScaling">The screen's own display scaling, to turn device pixels into the window's units.</param>
    public static Size RoomOf(PixelRect workArea, double screenScaling)
    {
        var scaling = screenScaling > 0 ? screenScaling : 1;
        return new Size(Math.Max(0, (workArea.Width / scaling) - FrameAllowance), Math.Max(0, (workArea.Height / scaling) - FrameAllowance));
    }

    /// <summary>Whether the screens keep the size they were built for at a step: <see cref="BaseMinimum"/> times the step fits the room.</summary>
    /// <param name="percent">The step in percent.</param>
    /// <param name="room">The screen's room from <see cref="RoomOf"/>, or null when the screen is not known, which leaves room for every step.</param>
    public static bool Fits(int percent, Size? room) =>
        room is not { } known
        || (BaseMinimum.Width * percent / 100.0 <= known.Width && BaseMinimum.Height * percent / 100.0 <= known.Height);

    /// <summary>The largest step that fits the room; the smallest step when none does, since that is as small as the interface gets.</summary>
    /// <param name="room">The screen's room from <see cref="RoomOf"/>, or null when the screen is not known.</param>
    public static int LargestFitting(Size? room) => Steps.Where(step => Fits(step, room)).DefaultIfEmpty(Steps[0]).Max();

    /// <summary>The step to draw at: the nearest step to what was wanted, lowered to the largest step at or below <paramref name="largestFitting"/>.</summary>
    /// <param name="wanted">The stored or chosen percent.</param>
    /// <param name="largestFitting">The largest step the screen has room for.</param>
    public static int Limit(int wanted, int largestFitting) =>
        Math.Min(Nearest(wanted), Steps.Where(step => step <= largestFitting).DefaultIfEmpty(Steps[0]).Max());

    /// <summary>The step closest to a stored percent; the lower step when it is exactly between two.</summary>
    /// <param name="percent">Any number, such as one typed into a settings file by hand.</param>
    public static int Nearest(int percent)
    {
        // Clamped first, so the distance below cannot overflow for a number far outside the steps.
        var clamped = Math.Clamp(percent, Steps[0], Steps[^1]);
        return Steps.OrderBy(step => Math.Abs(step - clamped)).ThenBy(step => step).First();
    }
}
