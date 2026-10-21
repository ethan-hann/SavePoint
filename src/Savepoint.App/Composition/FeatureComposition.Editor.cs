using Savepoint.App.Features.Common.Navigation;

namespace Savepoint.App.Composition;

public static partial class FeatureComposition
{
    /// <summary>The game editor pane factory, or null while the editor is not built yet.</summary>
    /// <param name="context">Shared App services.</param>
    public static IGameEditorFactory? GameEditor(FeatureContext context) => null;
}
