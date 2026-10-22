using Savepoint.App.Features.Help;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Features.Settings;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.App.Tests.Fakes;

namespace Savepoint.App.Tests.Features.Help;

// The page is a full pane the shell shows in place of the screen. It never destroys a pane that was open under it.
public class HowItWorksShellTests
{
    private sealed class Pane : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private static HowItWorksViewModel PageOf(ShellViewModel shell) => Assert.IsType<HowItWorksViewModel>(shell.Content);

    [Fact]
    public async Task Showing_the_help_puts_the_page_where_the_screen_was()
    {
        var rig = await ShellRig.StartAsync();

        rig.Shell.ShowHelp();

        Assert.IsType<HowItWorksViewModel>(rig.Shell.Content);
    }

    [Fact]
    public async Task Back_shows_the_screen_the_page_was_opened_from()
    {
        var rig = await ShellRig.StartAsync();
        var screen = rig.Shell.CurrentScreen;
        rig.Shell.ShowHelp();

        PageOf(rig.Shell).BackCommand.Execute(null);

        Assert.Same(screen, rig.Shell.Content);
    }

    [Fact]
    public async Task Showing_the_help_while_the_page_is_open_does_not_stack_a_second_page()
    {
        var rig = await ShellRig.StartAsync();
        var screen = rig.Shell.CurrentScreen;
        rig.Shell.ShowHelp();
        var page = PageOf(rig.Shell);

        rig.Shell.ShowHelp();
        Assert.Same(page, rig.Shell.Content);
        page.BackCommand.Execute(null);

        Assert.Same(screen, rig.Shell.Content);
    }

    [Fact]
    public async Task A_pane_that_was_open_is_kept_under_the_page_and_comes_back_with_its_work_intact()
    {
        var rig = await ShellRig.StartAsync();
        var editor = new Pane();
        rig.Shell.ShowPane(editor);

        rig.Shell.ShowHelp();
        Assert.IsType<HowItWorksViewModel>(rig.Shell.Content);
        Assert.False(editor.Disposed);
        PageOf(rig.Shell).BackCommand.Execute(null);

        Assert.Same(editor, rig.Shell.Content);
        Assert.False(editor.Disposed);
    }

    [Fact]
    public async Task Opening_another_pane_over_the_page_ends_both_the_page_and_the_pane_under_it()
    {
        var rig = await ShellRig.StartAsync();
        var editor = new Pane();
        var other = new Pane();
        rig.Shell.ShowPane(editor);
        rig.Shell.ShowHelp();

        rig.Shell.ShowPane(other);

        Assert.Same(other, rig.Shell.Content);
        Assert.True(editor.Disposed);
        Assert.False(other.Disposed);
    }

    [Fact]
    public async Task Disposing_the_shell_disposes_the_pane_that_is_under_the_page()
    {
        var rig = await ShellRig.StartAsync();
        var editor = new Pane();
        rig.Shell.ShowPane(editor);
        rig.Shell.ShowHelp();

        rig.Shell.Dispose();

        Assert.True(editor.Disposed);
    }

    [Fact]
    public async Task The_page_belongs_to_the_destination_it_was_opened_from()
    {
        var rig = await ShellRig.StartAsync();
        rig.Shell.ShowHelp();
        var page = PageOf(rig.Shell);

        rig.Shell.Navigate(NavDestination.History);
        Assert.IsNotType<HowItWorksViewModel>(rig.Shell.Content);
        rig.Shell.Navigate(NavDestination.Games);

        Assert.Same(page, rig.Shell.Content);
    }

    [Fact]
    public async Task The_page_opens_on_the_first_run_screen_and_back_returns_to_it()
    {
        var rig = await ShellRig.StartAsync(new FakeBackend(ScenarioBuilder.Empty().WithSettings(MockupScenario.Settings)));
        Assert.Same(rig.Shell.FirstRun, rig.Shell.Content);

        rig.Shell.FirstRun.HowItWorksCommand.Execute(null);
        Assert.IsType<HowItWorksViewModel>(rig.Shell.Content);
        PageOf(rig.Shell).BackCommand.Execute(null);

        Assert.Same(rig.Shell.FirstRun, rig.Shell.Content);
    }

    [Fact]
    public async Task The_Help_section_of_Settings_opens_the_page()
    {
        var rig = await SettingsRig.ShownAsync();

        rig.Screen.Help.ShowHowItWorksCommand.Execute(null);

        Assert.IsType<HowItWorksViewModel>(rig.Shell.Content);
    }
}
