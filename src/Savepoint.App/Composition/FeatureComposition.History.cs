using Savepoint.App.Features.History;
using Savepoint.App.Features.Shell;

namespace Savepoint.App.Composition;

public static partial class FeatureComposition
{
    /// <summary>The History screen's factory.</summary>
    /// <param name="context">Shared App services.</param>
    public static IScreenFactory HistoryScreen(FeatureContext context) => new HistoryScreenFactory(context, context.Clock);
}
