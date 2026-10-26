using Savepoint.App.Features.Launch;
using Savepoint.App.Features.Shell;
using Savepoint.App.Platform;

namespace Savepoint.App.Composition;

public static partial class FeatureComposition
{
    /// <summary>The shell-owned launch flow (D13.19), built once for the shell and kept above every screen.</summary>
    /// <param name="context">Shared App services.</param>
    public static ILaunchFlowHost LaunchFlow(FeatureContext context)
    {
        var api = context.Services;
        return new LaunchFlowViewModel(new LaunchDependencies(
            api.Launch,
            api.Profiles,
            api.Settings,
            context.SettingsWriter,
            api.Games,
            api.Jobs,
            context.Dialogs,
            context.Text,
            context.Toasts,
            context.Dispatcher,
            context.Clock,
            new JobAwaiter(api.Jobs),
            context.Attention ?? NullSessionAttention.Instance));
    }
}
