using Avalonia.Headless.XUnit;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Nothing in the app scrolls sideways or draws past its container. Every screen and dialog is opened in the states a person meets
// them and checked at the smallest window the app allows (640x420, which is also the layout at any interface size on a screen that
// holds it) and at the two scaled work areas D36 names, so a card, a label or a button that no longer fits fails here by name.
public class HorizontalOverflowAuditTests
{
    private static readonly (string Name, double Width, double Height)[] _sizes =
    [
        ("200% on 1080p", 960, 460),
        ("150% on 1366x768", 910, 432),
        ("smallest window", 640, 420),
    ];

    [AvaloniaTheory]
    [MemberData(nameof(AppStates.All), MemberType = typeof(AppStates))]
    public async Task Nothing_in_the_state_spills_sideways_at_a_scaled_or_the_smallest_window(string state)
    {
        using var app = await AppStates.OpenAsync(state);
        var problems = new List<string>();

        // Narrowest last: each size only shrinks the window, so a state keeps the scroll position its own opening gave it.
        foreach (var (name, width, height) in _sizes)
        {
            app.Window.Width = width;
            app.Window.Height = height;
            app.Settle();
            problems.AddRange(LayoutAudit.HorizontalProblems(app.Window).Select(problem => $"[{name}, {width}x{height}] {problem}"));
        }

        Assert.True(problems.Count == 0, $"{state}:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }
}
