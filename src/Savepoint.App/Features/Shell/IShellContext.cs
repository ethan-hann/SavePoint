using System.ComponentModel;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Shell;

/// <summary>A request, raised by the shell's "Add a game" buttons, to open the game editor.</summary>
/// <param name="Prefill">A definition to start from (a template or an imported file); null starts empty.</param>
public sealed record AddGameRequest(GameDefinition? Prefill);

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

    /// <summary>The launch flow, or null while none is plugged in.</summary>
    ILaunchFlowHost? LaunchFlow { get; }

    /// <summary>Raised after the game list, profile counts and sizes have been reloaded.</summary>
    event EventHandler? LibraryRefreshed;

    /// <summary>Raised when something asks for the game editor in create mode. The screen that owns the editor handles it, usually by calling <see cref="ShowPane"/>.</summary>
    event EventHandler<AddGameRequest>? AddGameRequested;

    /// <summary>Switches to a destination. The shell keeps a screen's state, so coming back finds it as it was.</summary>
    void Navigate(NavDestination destination);

    /// <summary>Selects a game by id; an unknown id is ignored.</summary>
    void SelectGame(string gameId);

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
}
