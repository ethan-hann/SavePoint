using Savepoint.App.Features.History;
using Savepoint.App.Features.Shell;

namespace Savepoint.App.Composition;

public static partial class FeatureComposition
{
    /// <summary>The History screen's factory.</summary>
    /// <param name="context">Shared App services.</param>
    public static IScreenFactory HistoryScreen(FeatureContext context) => new HistoryScreenFactory(HistoryDependenciesOf(context));

    /// <summary>What the History screen is built from.</summary>
    /// <param name="context">Shared App services.</param>
    /// <param name="clock">What "this week" is measured from; the context's clock when null.</param>
    public static HistoryDependencies HistoryDependenciesOf(FeatureContext context, TimeProvider? clock = null)
    {
        var services = context.Services;
        return new HistoryDependencies(services.History, services.Profiles, services.Games, services.Jobs, context.Dialogs, context.Text, context.Dispatcher, clock ?? context.Clock);
    }
}
