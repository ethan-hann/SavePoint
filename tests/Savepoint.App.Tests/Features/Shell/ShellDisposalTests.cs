using Savepoint.App.Features.Common.Search;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Features.EndToEnd;

namespace Savepoint.App.Tests.Features.Shell;

// A screen runs timers and reads of its own, so letting go of the shell lets go of each screen it built.
public class ShellDisposalTests
{
    [Fact]
    public async Task Disposing_the_shell_disposes_every_screen_it_built_that_holds_something_to_let_go_of()
    {
        var factories = new[] { new DisposableScreenFactory(NavDestination.Games), new DisposableScreenFactory(NavDestination.History) };
        var rig = await ShellRig.StartAsync(screens: factories);

        rig.Shell.Dispose();

        Assert.All(factories, factory => Assert.Equal(1, factory.Screen!.DisposedCount));
    }

    [Fact]
    public async Task A_screen_without_anything_to_let_go_of_is_left_alone_when_the_shell_is_disposed()
    {
        var factory = new RecordingScreenFactory(NavDestination.Settings);
        var rig = await ShellRig.StartAsync(screens: [factory]);

        var dispose = Record.Exception(rig.Shell.Dispose);

        Assert.Null(dispose);
    }

    [Fact]
    public async Task A_games_search_that_was_still_waiting_to_announce_says_nothing_once_the_shell_is_gone()
    {
        using var app = await ComposedApp.StartAsync();
        app.Games.ProfileSearch.Text = "vanilla";

        app.Main.Dispose();
        app.Clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Equal(string.Empty, app.Games.ProfileSearch.Announcement);
    }

    [Fact]
    public async Task The_history_screens_pending_search_timers_are_stopped_when_the_shell_is_disposed()
    {
        using var app = await ComposedApp.StartAsync();
        var idle = app.Clock.ActiveTimers;
        app.History.Search.Text = "blackreach";
        Assert.True(app.Clock.ActiveTimers > idle, "The search should be waiting on a timer before the shell goes.");

        app.Main.Dispose();

        Assert.True(app.Clock.ActiveTimers <= idle);
    }

    private sealed class DisposableScreen(NavDestination destination) : IScreen, IDisposable
    {
        public NavDestination Destination { get; } = destination;

        public int DisposedCount { get; private set; }

        public void OnShown()
        {
        }

        public void Dispose() => DisposedCount++;
    }

    private sealed class DisposableScreenFactory(NavDestination destination) : IScreenFactory
    {
        public NavDestination Destination { get; } = destination;

        public DisposableScreen? Screen { get; private set; }

        public IScreen Create(IShellContext shell) => Screen = new DisposableScreen(Destination);
    }
}
