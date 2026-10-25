using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.EndToEnd;

// A keyboard or screen reader user moving through the games must not lose their place when a background job ends and the library reloads.
public class FocusSurvivalTests
{
    private static async Task RefreshLibraryAsync(ComposedApp app)
    {
        var job = FakeAssert.Ok(await app.Backend.Games.OptimizeStorageAsync(SampleScenario.SkyrimId));
        app.Backend.Jobs.Complete(job.JobId);
        app.Settle();
    }

    [AvaloniaTheory]
    [InlineData("RailGames", ShellLayout.Sidebar)]
    [InlineData("ShelfGames", ShellLayout.Library)]
    public async Task Focus_on_a_game_survives_a_job_that_refreshes_the_library(string listName, ShellLayout layout)
    {
        using var app = await ComposedApp.StartWithViewAsync(layout: layout);
        var second = app.Find<ListBox>(listName).GetVisualDescendants().OfType<ListBoxItem>().ElementAt(1);
        second.Focus(NavigationMethod.Tab);
        app.Settle();
        Assert.Same(second, app.Focused);

        await RefreshLibraryAsync(app);

        var focused = Assert.IsAssignableFrom<ListBoxItem>(app.Focused);
        Assert.Contains(focused, app.Find<ListBox>(listName).GetVisualDescendants());
        Assert.Equal(SampleScenario.SatisfactoryId, ((GameRowViewModel)focused.DataContext!).Id);
    }
}
