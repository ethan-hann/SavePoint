using System.ComponentModel;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Shell;

/// <summary>A request, raised by the shell's "Add a game" buttons, to open the game editor.</summary>
/// <param name="Prefill">A definition to start from (a template or an imported file); null starts empty.</param>
public sealed record AddGameRequest(GameDefinition? Prefill);

/// <summary>A request to show the Activity screen, raised before the shell navigates to it.</summary>
/// <param name="GameId">The game whose jobs to show; null shows every game's jobs.</param>
public sealed record ActivityRequest(string? GameId);

/// <summary>
/// What a screen sees of the shell. Screens never reference the shell view model itself: they take this in
/// <see cref="IScreenFactory.Create"/>, read the selected game from it, and ask it to navigate, refresh or show a full pane.
/// </summary>
public interface IShellContext : INotifyPropertyChanged
{
    /// <summary>The game the rail or shelf has selected; null when the library is empty. Changes raise <see cref="INotifyPropertyChanged.PropertyChanged"/> with the name <c>SelectedGame</c>.</summary>
    GameDefinition? SelectedGame { get; }

    /// <summary>Every game in list order.</summary>
    IReadOnlyList<GameDefinition> Games { get; }

    /// <summary>The persisted layout.</summary>
    ShellLayout Layout { get; }

    /// <summary>The destination being shown.</summary>
    NavDestination Destination { get; }

    /// <summary>The Sidebar / Library toggle. Place it in a screen's header with <c>LayoutToggleView</c>; the shell draws it itself in the library top bar.</summary>
    LayoutToggleViewModel LayoutToggle { get; }

    /// <summary>The launch flow that owns starting a game.</summary>
    ILaunchFlowHost LaunchFlow { get; }

    /// <summary>Each game's artwork by game id; a game with none shows its name tint.</summary>
    Common.Art.IArtworkProvider Artwork { get; }

    /// <summary>Raised after the game list, profile counts and sizes have been reloaded.</summary>
    event EventHandler? LibraryRefreshed;

    /// <summary>Raised when something asks for the game editor in create mode. The screen that owns the editor handles it, usually by calling <see cref="ShowPane"/>.</summary>
    event EventHandler<AddGameRequest>? AddGameRequested;

    /// <summary>Raised by <see cref="ShowActivity"/> before the shell navigates, so the Activity screen can take its scope even when it is already shown.</summary>
    event EventHandler<ActivityRequest>? ActivityRequested;

    /// <summary>Switches to a destination. The shell keeps a screen's state, so coming back finds it as it was.</summary>
    void Navigate(NavDestination destination);

    /// <summary>Selects a game by id; an unknown id is ignored.</summary>
    void SelectGame(string gameId);

    /// <summary>Shows the Activity screen for a game, which is selected, or for every game when <paramref name="gameId"/> is null.</summary>
    /// <param name="gameId">The game id, or null for every game.</param>
    void ShowActivity(string? gameId);

    /// <summary>
    /// Reloads the games, their profile counts and sizes. Call it after anything that adds, edits or removes a game or
    /// a profile; the shell also does it by itself whenever a job finishes. Overlapping calls collapse into one more reload.
    /// </summary>
    Task RefreshLibraryAsync();

    /// <summary>Saves a new layout with the rest of the settings unchanged and switches to it. A failed save puts the old layout back and tells the user.</summary>
    Task SetLayoutAsync(ShellLayout layout);

    /// <summary>Tells the shell the settings were saved elsewhere (the Settings screen), so it follows the layout and the theme.</summary>
    void SettingsChanged(AppSettings settings);

    /// <summary>Asks for the game editor in create mode: switches to Games and raises <see cref="AddGameRequested"/>.</summary>
    void RequestAddGame(GameDefinition? prefill = null);

    /// <summary>
    /// Replaces the current destination's screen with a full pane (the game editor) until <see cref="ClosePane"/>. The pane's
    /// view is found by <see cref="Common.ViewLocator"/>. It belongs to the destination it was opened from: navigating away hides it and returning shows it again.
    /// </summary>
    void ShowPane(object paneViewModel);

    /// <summary>Closes the full pane, if one is open.</summary>
    void ClosePane();

    /// <summary>
    /// Opens the "How SavePoint works" page as a full pane. A pane that is open (the game editor, with a form half filled in) is kept
    /// underneath and comes back when the page closes. Does nothing while the page is already open.
    /// </summary>
    void ShowHelp();

    /// <summary>
    /// Opens the "How SavePoint works" page at one topic, the same way <see cref="ShowHelp()"/> opens it: a pane that is open is kept
    /// underneath. While the page is already open it moves to the topic instead.
    /// </summary>
    /// <param name="section">The topic to show.</param>
    void ShowHelp(Help.HowItWorksSection section);

    /// <summary>True when the first-run tour can run now: the selected game has a profile for it to point at.</summary>
    bool CanShowTour { get; }

    /// <summary>
    /// Shows the first-run tour from its first step: closes the page or pane that is open, goes to the Games screen and starts. It does
    /// nothing while <see cref="CanShowTour"/> is false.
    /// </summary>
    void StartTour();
}
