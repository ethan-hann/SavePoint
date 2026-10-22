using Savepoint.App.Features.Activity;
using Savepoint.App.Features.Shell;

namespace Savepoint.App.Composition;

public static partial class FeatureComposition
{
    /// <summary>The Activity screen's factory.</summary>
    /// <param name="context">Shared App services.</param>
    public static IScreenFactory ActivityScreen(FeatureContext context) =>
        new ActivityScreenFactory(new ActivityDependencies(context.Services.Jobs, context.Dialogs, context.Text, context.Dispatcher, context.Clock));
}
