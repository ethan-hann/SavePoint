using System.Reflection;
using Avalonia.Headless.XUnit;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Games;

// The window coming to the front is the fallback for folders that cannot be watched, so the view has to pass it on.
public class GamesScreenActivationViewTests
{
    // The headless platform never activates a window by itself, so this calls the callback a real platform would.
    private static void PlatformActivates(Avalonia.Controls.Window window)
    {
        var impl = window.PlatformImpl ?? throw new InvalidOperationException("The window has no platform implementation.");
        var property = impl.GetType().GetProperty("Activated", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var callback = (Action?)property?.GetValue(impl) ?? throw new InvalidOperationException("No Activated callback.");
        callback();
    }

    [AvaloniaFact]
    public async Task Bringing_the_window_to_the_front_reads_the_profiles_status_again()
    {
        var clock = new ManualTimeProvider();
        using var harness = await GamesViewHarness.StartAsync(clock: clock);
        var reads = harness.Rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.GetRowStatsAsync)).Count;

        PlatformActivates(harness.Window);
        clock.Advance(Savepoint.App.Features.Games.GamesScreenViewModel.ChangeSettleTime);
        harness.Settle();

        Assert.Equal(reads + 1, harness.Rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.GetRowStatsAsync)).Count);
    }
}
