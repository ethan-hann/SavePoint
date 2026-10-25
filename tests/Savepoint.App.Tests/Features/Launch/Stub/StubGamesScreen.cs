using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Launch.Stub;

// Test-only stand-in for the Games screen: a header, a profile table with Play buttons that start the launch flow, and the flow's inline content below.
public sealed class StubGamesScreenFactory : IScreenFactory
{
    public NavDestination Destination => NavDestination.Games;

    public IScreen Create(IShellContext shell) => new StubGamesScreenViewModel(shell);
}

public sealed record StubProfileRow(string Name, string Secondary, bool IsActive, bool HasUnsaved, string Version, string Mods, string LastPlayed, LaunchTarget Target);

public sealed partial class StubGamesScreenViewModel : ObservableObject, IScreen
{
    private readonly IShellContext _shell;

    public StubGamesScreenViewModel(IShellContext shell)
    {
        _shell = shell;
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IShellContext.SelectedGame))
            {
                Refresh();
            }
        };
        if (shell.LaunchFlow is { } flow)
        {
            flow.InlineContentChanged += (_, _) => Refresh();
            flow.PropertyChanged += (_, _) => Refresh();
        }

        Rows =
        [
            new("Modded playthrough - 7-2-26", "Loaded now · 4 snapshots", true, false, "1.6.640", "212", "3 days ago", new LaunchTarget.ProfileTip(SampleScenario.ModdedId)),
            new("Vanilla - latest update", "1 snapshot", false, false, "1.6.1170", "-", "6 weeks ago", new LaunchTarget.ProfileTip(SampleScenario.VanillaId)),
            new("Survival test - 5-14-26", "New changes · 3 snapshots", false, true, "1.6.640", "189", "12 weeks ago", new LaunchTarget.ProfileTip(SampleScenario.SurvivalId)),
        ];
        PlayCommand = new AsyncRelayCommand<StubProfileRow>(PlayAsync);
    }

    public NavDestination Destination => NavDestination.Games;

    public IReadOnlyList<StubProfileRow> Rows { get; }

    public IAsyncRelayCommand<StubProfileRow> PlayCommand { get; }

    public string GameName => _shell.SelectedGame?.Name ?? string.Empty;

    public string InstallPath => _shell.SelectedGame?.Install.Path ?? string.Empty;

    public object? InlineContent => _shell.SelectedGame is { } game ? _shell.LaunchFlow?.InlineContentFor(game.Id) : null;

    public bool CanPlay => _shell.SelectedGame is { } game && _shell.LaunchFlow?.IsActive(game.Id) != true;

    public void OnShown() => Refresh();

    private void Refresh()
    {
        OnPropertyChanged(nameof(GameName));
        OnPropertyChanged(nameof(InstallPath));
        OnPropertyChanged(nameof(InlineContent));
        OnPropertyChanged(nameof(CanPlay));
    }

    private async Task PlayAsync(StubProfileRow? row)
    {
        if (row is not null && _shell.SelectedGame is { } game && _shell.LaunchFlow is { } flow)
        {
            await flow.BeginAsync(game.Id, row.Target);
        }
    }
}
