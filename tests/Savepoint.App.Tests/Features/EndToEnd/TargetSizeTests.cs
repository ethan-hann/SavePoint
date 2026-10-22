using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace Savepoint.App.Tests.Features.EndToEnd;

// An icon-only button is a small target for a hand that is not steady, so none is smaller than 32 by 32 anywhere in the app.
public class TargetSizeTests
{
    private const double Smallest = 32;

    [AvaloniaTheory]
    [MemberData(nameof(AllStates))]
    public async Task Icon_only_buttons_are_at_least_32_by_32(string state)
    {
        using var app = await AppStates.OpenAsync(state);
        app.Settle();

        var small = app.All<Button>()
            .Where(button => button is not RepeatButton && button.Classes.Contains("icon") && button.IsEffectivelyVisible)
            .Where(button => button.Bounds.Width < Smallest - 0.5 || button.Bounds.Height < Smallest - 0.5)
            .Select(button => $"{Avalonia.Automation.AutomationProperties.GetName(button)} is {button.Bounds.Width:0}x{button.Bounds.Height:0}")
            .ToList();

        Assert.True(small.Count == 0, $"{state}: {string.Join("; ", small)}");
    }

    public static TheoryData<string> AllStates() => AppStates.All;
}
