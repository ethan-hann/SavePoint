using Savepoint.App.Hosting;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Hosting;

// The window opens at the saved interface size, so the size is read before the window is built, without ever holding up the start.
public class StartupUiScaleTests
{
    private static FakeBackend BackendWithScale(int percent) =>
        new(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { UiScalePercent = percent }));

    [Fact]
    public void The_saved_size_is_read_before_the_window_opens()
    {
        Assert.Equal(150, StartupUiScale.Read(BackendWithScale(150).Settings));
    }

    [Fact]
    public void A_saved_number_that_is_no_step_reads_as_the_nearest_step()
    {
        Assert.Equal(125, StartupUiScale.Read(BackendWithScale(137).Settings));
    }

    [Fact]
    public void Settings_that_cannot_be_read_leave_the_interface_at_100_percent()
    {
        var backend = BackendWithScale(150);
        backend.FailNext<ISettingsApi>(nameof(ISettingsApi.GetAsync), ErrorInfo.From(new InvalidOperationException("No settings.")));

        Assert.Equal(100, StartupUiScale.Read(backend.Settings));
    }

    [Fact]
    public void A_read_that_takes_too_long_leaves_the_interface_at_100_percent_instead_of_holding_up_the_start()
    {
        var never = new TaskCompletionSource<OperationResult<AppSettings>>();

        Assert.Equal(100, StartupUiScale.Read(() => never.Task, TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void A_read_that_throws_leaves_the_interface_at_100_percent()
    {
        Assert.Equal(100, StartupUiScale.Read(() => throw new InvalidOperationException("Broken."), TimeSpan.FromSeconds(1)));
    }
}
