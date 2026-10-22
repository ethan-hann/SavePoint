using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Backend;

public class HelpProgressSettingsTests
{
    [Fact]
    public async Task What_the_help_has_explained_comes_back_after_a_restart_and_saving_something_else_keeps_it()
    {
        using var workspace = new BackendWorkspace();
        var first = (await workspace.Settings.GetAsync(workspace.Token)).Value!;

        var saved = await workspace.Settings.SaveAsync(first with { OlderChoiceHelpSeen = true, DiscardHelpSeen = true });
        var later = await workspace.Settings.SaveAsync(saved.Value! with { Theme = ThemePreference.Dark });
        var restarted = (await workspace.Restart().Settings.GetAsync(workspace.Token)).Value!;

        Assert.False(first.OlderChoiceHelpSeen);
        Assert.True(saved.Ok, saved.Error?.Detail);
        Assert.True(later.Ok, later.Error?.Detail);
        Assert.True(restarted.OlderChoiceHelpSeen);
        Assert.True(restarted.DiscardHelpSeen);
        Assert.False(restarted.ReplaceHelpSeen);
        Assert.Equal(ThemePreference.Dark, restarted.Theme);
    }

    [Fact]
    public async Task That_the_tour_has_run_comes_back_after_a_restart()
    {
        using var workspace = new BackendWorkspace();
        var first = (await workspace.Settings.GetAsync(workspace.Token)).Value!;

        var saved = await workspace.Settings.SaveAsync(first with { TourSeen = true });
        var restarted = (await workspace.Restart().Settings.GetAsync(workspace.Token)).Value!;

        Assert.False(first.TourSeen);
        Assert.True(saved.Ok, saved.Error?.Detail);
        Assert.True(restarted.TourSeen);
    }
}
