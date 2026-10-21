using Savepoint.App.Features.Common.Navigation;
using Savepoint.App.Features.Shell;

namespace Savepoint.App.Composition;

public static partial class FeatureComposition
{
    /// <summary>The Games screen's factory, or null while the screen is not built yet (the shell then shows its placeholder).</summary>
    /// <param name="context">Shared App services.</param>
    /// <param name="editor">Builds the game editor pane; null while the editor is not built yet.</param>
    public static IScreenFactory? GamesScreen(FeatureContext context, IGameEditorFactory? editor) => null;
}
