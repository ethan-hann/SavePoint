using Savepoint.App.Features.Common.Navigation;
using Savepoint.App.Features.Editor;

namespace Savepoint.App.Composition;

public static partial class FeatureComposition
{
    /// <summary>The game editor pane factory the Games screen opens the editor with.</summary>
    /// <param name="context">Shared App services.</param>
    public static IGameEditorFactory? GameEditor(FeatureContext context) =>
        new GameEditorFactory(context.Services.Games, context.Dialogs, context.Text, context.Toasts, context.Services.Artwork);
}
