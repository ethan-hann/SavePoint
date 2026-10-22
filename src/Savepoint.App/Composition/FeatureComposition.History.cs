using Savepoint.App.Features.Common.Navigation;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Shell;

namespace Savepoint.App.Composition;

public static partial class FeatureComposition
{
    /// <summary>The History screen's factory.</summary>
    /// <param name="context">Shared App services.</param>
    /// <param name="editor">Builds the game editor pane; History never opens it.</param>
    public static IScreenFactory HistoryScreen(FeatureContext context, IGameEditorFactory? editor)
    {
        _ = editor;
        return new HistoryScreenFactory(context, context.Clock);
    }
}
