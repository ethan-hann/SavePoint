using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Navigation;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Shell;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Editor;

/// <summary>Builds the game editor pane for the Games screen.</summary>
public sealed class GameEditorFactory : IGameEditorFactory
{
    private readonly IGamesApi _games;
    private readonly IDialogs _dialogs;
    private readonly UiFormatter _text;
    private readonly ToastHostViewModel _toasts;

    /// <param name="games">The games Api the editor saves through.</param>
    /// <param name="dialogs">Pickers and confirmations.</param>
    /// <param name="text">Formats the editor's copy.</param>
    /// <param name="toasts">Reports the outcome of import and export.</param>
    public GameEditorFactory(IGamesApi games, IDialogs dialogs, UiFormatter text, ToastHostViewModel toasts)
    {
        _games = games;
        _dialogs = dialogs;
        _text = text;
        _toasts = toasts;
    }

    /// <summary>Creates the editor for a game in the shell's library, and starts reading its folders' status.</summary>
    /// <param name="shell">The shell the pane is shown in.</param>
    /// <param name="gameId">The game to edit.</param>
    /// <exception cref="ArgumentException">The shell's library has no game with that id.</exception>
    public object CreateForExisting(IShellContext shell, string gameId)
    {
        var game = shell.Games.FirstOrDefault(candidate => candidate.Id == gameId)
            ?? throw new ArgumentException($"The library has no game '{gameId}'.", nameof(gameId));
        var editor = GameEditorViewModel.ForExisting(_games, _dialogs, _text, _toasts, shell, game);
        _ = editor.LoadAsync();
        return editor;
    }

    /// <inheritdoc />
    public object CreateForNew(IShellContext shell, GameDefinition? prefill)
    {
        var editor = GameEditorViewModel.ForNew(_games, _dialogs, _text, _toasts, shell, prefill);
        _ = editor.LoadAsync();
        return editor;
    }
}
