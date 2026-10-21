using Savepoint.App.Features.Shell;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Common.Navigation;

/// <summary>
/// Builds the game editor pane. The Games screen opens it through <see cref="IShellContext.ShowPane"/> and the editor closes
/// itself with <see cref="IShellContext.ClosePane"/> when the user saves or goes back.
/// </summary>
public interface IGameEditorFactory
{
    /// <summary>Creates the editor for an existing game, loaded from the Api by id.</summary>
    /// <param name="shell">The shell the pane is shown in; the editor uses it to close itself and to refresh the library after saving.</param>
    /// <param name="gameId">The game to edit.</param>
    /// <returns>The pane view model; its view is found by name.</returns>
    object CreateForExisting(IShellContext shell, string gameId);

    /// <summary>Creates the editor for a new game.</summary>
    /// <param name="shell">The shell the pane is shown in.</param>
    /// <param name="prefill">A template or imported definition to start from; null starts empty. Nothing is saved until the user saves.</param>
    /// <returns>The pane view model; its view is found by name.</returns>
    object CreateForNew(IShellContext shell, GameDefinition? prefill);
}
