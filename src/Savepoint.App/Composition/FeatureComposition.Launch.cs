using Savepoint.App.Features.Shell;

namespace Savepoint.App.Composition;

/// <summary>
/// Where each feature wires its view models. Each feature owns exactly one <c>FeatureComposition.&lt;Feature&gt;.cs</c>
/// file, so features are added without touching each other's code or <see cref="AppComposition"/>.
/// </summary>
public static partial class FeatureComposition
{
    /// <summary>The shell-owned launch flow (D13.19), or null while it is not built yet.</summary>
    /// <param name="context">Shared App services.</param>
    public static ILaunchFlowHost? LaunchFlow(FeatureContext context) => null;
}
